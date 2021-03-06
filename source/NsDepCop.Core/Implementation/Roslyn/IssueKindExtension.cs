﻿using Codartis.NsDepCop.Core.Interface;
using Microsoft.CodeAnalysis;

namespace Codartis.NsDepCop.Core.Implementation.Roslyn
{
    /// <summary>
    /// Implements extension methods for Codartis.NsDepCop.Core.IssueKind.
    /// </summary>
    public static class IssueKindExtension
    {
        /// <summary>
        /// Translates from Codartis.NsDepCop.Core.IssueKind to Microsoft.CodeAnalysis.DiagnosticSeverity.
        /// </summary>
        /// <param name="issueKind">A Codartis.NsDepCop.Core.IssueKind value.</param>
        /// <returns>A Microsoft.CodeAnalysis.DiagnosticSeverity value.</returns>
        public static DiagnosticSeverity ToDiagnosticSeverity(this IssueKind issueKind)
        {
            return IssueKindTranslator.ToDiagnosticSeverity(issueKind);
        }
    }
}
