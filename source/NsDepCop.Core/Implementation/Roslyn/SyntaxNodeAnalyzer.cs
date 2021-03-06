﻿using System.Collections.Generic;
using System.Linq;
using Codartis.NsDepCop.Core.Interface;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codartis.NsDepCop.Core.Implementation.Roslyn
{
    /// <summary>
    /// Static helper class that implements the dependency analysis logic for a syntax node.
    /// </summary>
    public static class SyntaxNodeAnalyzer
    {
        /// <summary>
        /// Performs namespace dependency analysis for a SyntaxNode.
        /// </summary>
        /// <param name="node">A syntax node.</param>
        /// <param name="semanticModel">The semantic model of the current document.</param>
        /// <param name="typeDependencyValidator">The validator that decides whether a dependency is allowed.</param>
        /// <returns>A list of dependency violations. Can be empty.</returns>
        public static IEnumerable<DependencyViolation> Analyze(SyntaxNode node, SemanticModel semanticModel, ITypeDependencyValidator typeDependencyValidator)
        {
            // Determine the type that contains the current syntax node.
            var enclosingType = DetermineEnclosingType(node, semanticModel);
            if (enclosingType?.ContainingNamespace == null)
                yield break;

            // Determine the type referenced by the symbol represented by the current syntax node.
            var referencedType = DetermineReferencedType(node, semanticModel);
            var referencedTypeDependencyViolation = ValidateDependency(enclosingType, referencedType, node, typeDependencyValidator);
            if (referencedTypeDependencyViolation != null)
                yield return referencedTypeDependencyViolation;

            // If this is an extension method invocation then determine the type declaring the extension method.
            var declaringType = DetermineExtensionMethodDeclaringType(node, semanticModel);
            var declaringTypeDependencyViolation = ValidateDependency(enclosingType, declaringType, node, typeDependencyValidator);
            if (declaringTypeDependencyViolation != null)
                yield return declaringTypeDependencyViolation;
        }

        /// <summary>
        /// Validates whether a type is allowed to reference another. Returns a DependencyViolation if not allowed.
        /// </summary>
        /// <param name="fromType">The referring type.</param>
        /// <param name="toType">The referenced type.</param>
        /// <param name="node">The syntax node currently analyzed.</param>
        /// <param name="typeDependencyValidator">The validator that decides whether a dependency is allowed.</param>
        /// <returns>A DependencyViolation if the dependency is not allowed. Null otherwise.</returns>
        private static DependencyViolation ValidateDependency(ITypeSymbol fromType, ITypeSymbol toType,
            SyntaxNode node, ITypeDependencyValidator typeDependencyValidator)
        {
            if (fromType == null || 
                toType?.ContainingNamespace == null || 
                toType.TypeKind == TypeKind.Error)
                return null;

            var typeDependency = new TypeDependency(
                fromType.ContainingNamespace.ToDisplayString(), fromType.MetadataName, 
                toType.ContainingNamespace.ToDisplayString(), toType.MetadataName);

            return typeDependencyValidator.IsAllowedDependency(typeDependency) 
                ? null 
                : new DependencyViolation(typeDependency, GetSourceSegment(node));
        }

        /// <summary>
        /// Determines the type declaring the given extension method syntax node.
        /// </summary>
        /// <param name="node">A syntax node representing an extension method.</param>
        /// <param name="semanticModel">The semantic model of the project.</param>
        /// <returns>The type declaring the given extension method syntax node, or null if not found.</returns>
        private static ITypeSymbol DetermineExtensionMethodDeclaringType(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;

            return methodSymbol == null || !methodSymbol.IsExtensionMethod 
                ? null 
                : methodSymbol.ContainingType;
        }

        /// <summary>
        /// Determines the type referenced by the given syntax node.
        /// </summary>
        /// <param name="node">A syntax node.</param>
        /// <param name="semanticModel">The semantic model of the project.</param>
        /// <returns>The type referenced by the given syntax node, or null if no type was referenced.</returns>
        private static ITypeSymbol DetermineReferencedType(SyntaxNode node, SemanticModel semanticModel)
        {
            var typeSymbol = semanticModel.GetTypeInfo(node).Type;
            if (typeSymbol != null)
                return typeSymbol;

            // Special case (or Roslyn bug?): 
            // if we have an IdentifierNameSyntax inside an ObjectCreationExpression then 
            // semanticModel.GetTypeInfo(node).Type returns null but
            // semanticModel.GetSymbolInfo(node).Symbol returns the expected ITypeSymbol
            var symbolInfo = semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol is ITypeSymbol)
                return symbolInfo.Symbol as ITypeSymbol;

            // Special case: for method invocations we should check the return type
            if (symbolInfo.Symbol is IMethodSymbol)
                return (symbolInfo.Symbol as IMethodSymbol).ReturnType;

            // Could not determine referenced type.
            return null;
        }

        /// <summary>
        /// Determines the type that contains the given syntax node.
        /// </summary>
        /// <param name="node">A syntax node.</param>
        /// <param name="semanticModel">The semantic model of the project.</param>
        /// <returns>The type that contains the given syntax node. Or null if can't determine.</returns>
        private static ITypeSymbol DetermineEnclosingType(SyntaxNode node, SemanticModel semanticModel)
        {
            // Find the type declaration that contains the current syntax node.
            var typeDeclarationSyntaxNode = node.Ancestors().FirstOrDefault(i => i is TypeDeclarationSyntax);
            if (typeDeclarationSyntaxNode == null)
                return null;

            // Determine the type of the type declaration that contains the current syntax node.
            return semanticModel.GetDeclaredSymbol(typeDeclarationSyntaxNode) as ITypeSymbol;
        }

        /// <summary>
        /// Gets the source segment of the given syntax node.
        /// </summary>
        /// <param name="syntaxNode">A syntax node.</param>
        /// <returns>The source segment of the given syntax node.</returns>
        private static SourceSegment GetSourceSegment(SyntaxNode syntaxNode)
        {
            var syntaxNodeOrToken = GetNodeOrTokenToReport(syntaxNode);

            var lineSpan = syntaxNodeOrToken.GetLocation().GetLineSpan();

            return new SourceSegment
            (
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                lineSpan.EndLinePosition.Line + 1,
                lineSpan.EndLinePosition.Character + 1,
                syntaxNodeOrToken.ToString(),
                lineSpan.Path
            );
        }

        /// <summary>
        /// Determine which node or token should be reported as the location of the issue.
        /// </summary>
        /// <param name="syntaxNode">The syntax node that caused the issue.</param>
        /// <returns>The node or token that should be reported as the location of the issue.</returns>
        private static SyntaxNodeOrToken GetNodeOrTokenToReport(SyntaxNode syntaxNode)
        {
            SyntaxNodeOrToken syntaxNodeOrToken = syntaxNode;

            // For a Generic Name we should report its first token as the location.
            if (syntaxNode is GenericNameSyntax)
            {
                syntaxNodeOrToken = syntaxNode.GetFirstToken();
            }

            return syntaxNodeOrToken;
        }
    }
}
