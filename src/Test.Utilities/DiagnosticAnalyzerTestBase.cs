﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Analyzer.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Roslyn.Utilities;
using Xunit;

namespace Test.Utilities
{
    public abstract class DiagnosticAnalyzerTestBase
    {
        [Flags]
        protected enum ReferenceFlags
        {
            None = 0b000,
            RemoveCodeAnalysis = 0b001,
            RemoveImmutable = 0b010,
            RemoveSystemData = 0b100
        }

        protected static readonly CompilationOptions s_CSharpDefaultOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        protected static readonly CompilationOptions s_CSharpUnsafeCodeDefaultOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true);
        protected static readonly CompilationOptions s_visualBasicDefaultOptions = new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        internal const string DefaultFilePathPrefix = "Test";
        internal const string CSharpDefaultFileExt = "cs";
        internal const string VisualBasicDefaultExt = "vb";
        internal static readonly string CSharpDefaultFilePath = DefaultFilePathPrefix + 0 + "." + CSharpDefaultFileExt;
        internal static readonly string VisualBasicDefaultFilePath = DefaultFilePathPrefix + 0 + "." + VisualBasicDefaultExt;

        private const string TestProjectName = "TestProject";

        protected const TestValidationMode DefaultTestValidationMode = TestValidationMode.AllowCompileWarnings;

        /// <summary>
        /// Return the C# diagnostic analyzer to get analyzer diagnostics.
        /// This may return null when used in context of verifying a code fix for compiler diagnostics.
        /// </summary>
        /// <returns></returns>
        protected abstract DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer();

        /// <summary>
        /// Return the VB diagnostic analyzer to get analyzer diagnostics.
        /// This may return null when used in context of verifying a code fix for compiler diagnostics.
        /// </summary>
        /// <returns></returns>
        protected abstract DiagnosticAnalyzer GetBasicDiagnosticAnalyzer();

        protected bool PrintActualDiagnosticsOnFailure { get; set; }

        // It is assumed to be of the format, Get<RuleId>CSharpResultAt(line: {0}, column: {1}, message: {2})
        private string ExpectedDiagnosticsAssertionTemplate { get; set; }

        protected static DiagnosticResult GetGlobalResult(string id, string message)
        {
            return new DiagnosticResult(id, DiagnosticHelpers.DefaultDiagnosticSeverity).WithMessage(message);
        }

        protected static DiagnosticResult GetGlobalResult(DiagnosticDescriptor rule, params string[] messageArguments)
        {
            return new DiagnosticResult(rule).WithMessage(string.Format(rule.MessageFormat.ToString(), messageArguments));
        }

        protected static DiagnosticResult GetBasicResultAt(int line, int column, string id, string message)
        {
            return GetResultAt(VisualBasicDefaultFilePath, line, column, id, message);
        }

        protected static DiagnosticResult GetBasicResultAt(int line, int column, DiagnosticDescriptor rule, params object[] messageArguments)
        {
            return GetResultAt(VisualBasicDefaultFilePath, line, column, rule, messageArguments);
        }

        protected static DiagnosticResult GetCSharpResultAt(int line, int column, string id, string message)
        {
            return GetResultAt(CSharpDefaultFilePath, line, column, id, message);
        }

        protected static DiagnosticResult GetCSharpResultAt(string id, string message, params string[] locationStrings)
        {
            return GetResultAt(CSharpDefaultFilePath, id, message, locationStrings);
        }

        protected static DiagnosticResult GetCSharpResultAt(int line, int column, DiagnosticDescriptor rule, params object[] messageArguments)
        {
            return GetResultAt(CSharpDefaultFilePath, line, column, rule, messageArguments);
        }

        protected static DiagnosticResult GetAdditionalFileResultAt(int line, int column, string additionalFilePath, DiagnosticDescriptor rule, params object[] messageArguments)
        {
            return GetResultAt(additionalFilePath, line, column, rule, messageArguments);
        }

        private static DiagnosticResult GetResultAt(string path, int line, int column, string id, string message)
        {
            return new DiagnosticResult(id, DiagnosticHelpers.DefaultDiagnosticSeverity).WithLocation(path, line, column).WithMessage(message);
        }

        protected static DiagnosticResult GetResultAt(string path, string id, string message, params string[] locationStrings)
        {
            var result = new DiagnosticResult(id, DiagnosticHelpers.DefaultDiagnosticSeverity).WithMessage(message);
            foreach (var location in ParseResultLocations(path, locationStrings))
            {
                result = result.WithLocation(location.path, location.location);
            }

            return result;
        }

        private static DiagnosticResult GetResultAt(string path, int line, int column, DiagnosticDescriptor rule, params object[] messageArguments)
        {
            return new DiagnosticResult(rule).WithLocation(path, line, column).WithArguments(messageArguments);
        }

        private static (string path, LinePosition location)[] ParseResultLocations(string defaultPath, string[] locationStrings)
        {
            var builder = new List<(string path, LinePosition location)>();

            foreach (string str in locationStrings)
            {
                string[] tokens = str.Split('(', ',', ')');
                Assert.True(tokens.Length == 4, "Location string must be of the format 'FileName.cs(line,column)' or just '(line,column)' to use " + defaultPath + " as the file name.");

                string path = tokens[0].Length == 0 ? defaultPath : tokens[0];

                Assert.True(int.TryParse(tokens[1], out int line) && line >= -1, "Line must be >= -1 in location string: " + str);

                Assert.True(int.TryParse(tokens[2], out int column) && line >= -1, "Column must be >= -1 in location string: " + str);

                builder.Add((path, new LinePosition(line - 1, column - 1)));
            }

            return builder.ToArray();
        }

        protected void VerifyCSharpUnsafeCode(string source, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, true, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, CompilationOptions compilationOptions = null, ParseOptions parseOptions = null, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions, parseOptions, expected: expected);
        }

        protected void VerifyCSharp(string source, TestValidationMode validationMode, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), validationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, ReferenceFlags referenceFlags, params DiagnosticResult[] expected)
        {
            Verify(source, LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), referenceFlags, DefaultTestValidationMode, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, TestValidationMode validationMode, CompilationOptions compilationOptions = null, ParseOptions parseOptions = null, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), validationMode, false, ReferenceFlags.None, compilationOptions, parseOptions, expected: expected);
        }

        protected void VerifyCSharp(string source, ReferenceFlags referenceFlags, TestValidationMode validationMode = DefaultTestValidationMode, params DiagnosticResult[] expected)
        {
            Verify(source, LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), referenceFlags, validationMode, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string[] sources, params DiagnosticResult[] expected)
        {
            Verify(sources.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string[] sources, ReferenceFlags referenceFlags, params DiagnosticResult[] expected)
        {
            Verify(sources.ToFileAndSource(), LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, false, referenceFlags, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(FileAndSource[] sources, params DiagnosticResult[] expected)
        {
            Verify(sources, LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, FileAndSource additionalText, params DiagnosticResult[] expected)
        {
            VerifyCSharp(source, additionalText, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyCSharp(string source, FileAndSource additionalText, CompilationOptions compilationOptions, ParseOptions parseOptions, params DiagnosticResult[] expected)
        {
            var additionalFiles = GetAdditionalTextFiles(additionalText.FilePath, additionalText.Source);
            Verify(source, LanguageNames.CSharp, GetBasicDiagnosticAnalyzer(), additionalFiles, compilationOptions, parseOptions, expected);
        }

        protected void VerifyBasic(string source, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string source, CompilationOptions compilationOptions = null, ParseOptions parseOptions = null, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions, parseOptions, expected: expected);
        }

        protected void VerifyBasic(string source, TestValidationMode validationMode, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), validationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string source, TestValidationMode validationMode, CompilationOptions compilationOptions = null, ParseOptions parseOptions = null, params DiagnosticResult[] expected)
        {
            Verify(new[] { source }.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), validationMode, false, ReferenceFlags.None, compilationOptions, parseOptions, expected: expected);
        }

        protected void VerifyBasic(string source, ReferenceFlags referenceFlags, params DiagnosticResult[] expected)
        {
            Verify(source, LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), referenceFlags, DefaultTestValidationMode, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string source, ReferenceFlags referenceFlags, TestValidationMode validationMode = DefaultTestValidationMode, params DiagnosticResult[] expected)
        {
            Verify(source, LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), referenceFlags, validationMode, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string[] sources, params DiagnosticResult[] expected)
        {
            Verify(sources.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string[] sources, ReferenceFlags referenceFlags, params DiagnosticResult[] expected)
        {
            Verify(sources.ToFileAndSource(), LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), DefaultTestValidationMode, false, referenceFlags, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(FileAndSource[] sources, params DiagnosticResult[] expected)
        {
            Verify(sources, LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), DefaultTestValidationMode, false, ReferenceFlags.None, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string source, FileAndSource additionalText, params DiagnosticResult[] expected)
        {
            VerifyBasic(source, additionalText, compilationOptions: null, parseOptions: null, expected: expected);
        }

        protected void VerifyBasic(string source, FileAndSource additionalText, CompilationOptions compilationOptions, ParseOptions parseOptions, params DiagnosticResult[] expected)
        {
            var additionalFiles = GetAdditionalTextFiles(additionalText.FilePath, additionalText.Source);
            Verify(source, LanguageNames.VisualBasic, GetBasicDiagnosticAnalyzer(), additionalFiles, compilationOptions, parseOptions, expected);
        }

        protected void Verify(string source, string language, DiagnosticAnalyzer analyzer, IEnumerable<TestAdditionalDocument> additionalFiles, CompilationOptions compilationOptions, ParseOptions parseOptions, params DiagnosticResult[] expected)
        {
            var diagnostics = GetSortedDiagnostics(new[] { source }.ToFileAndSource(), language, analyzer, compilationOptions, parseOptions, additionalFiles: additionalFiles);
            diagnostics.Verify(analyzer, PrintActualDiagnosticsOnFailure, ExpectedDiagnosticsAssertionTemplate, GetDefaultPath(language), expected);
        }

        private void Verify(string source, string language, DiagnosticAnalyzer analyzer, ReferenceFlags referenceFlags, TestValidationMode validationMode, CompilationOptions compilationOptions, ParseOptions parseOptions, params DiagnosticResult[] expected)
        {
            var diagnostics = GetSortedDiagnostics(new[] { source }.ToFileAndSource(), language, analyzer, compilationOptions, parseOptions, referenceFlags: referenceFlags, validationMode: validationMode);
            diagnostics.Verify(analyzer, PrintActualDiagnosticsOnFailure, ExpectedDiagnosticsAssertionTemplate, GetDefaultPath(language), expected);
        }

        private void Verify(FileAndSource[] sources, string language, DiagnosticAnalyzer analyzer, TestValidationMode validationMode, bool allowUnsafeCode, ReferenceFlags referenceFlags, CompilationOptions compilationOptions, ParseOptions parseOptions, params DiagnosticResult[] expected)
        {
            var diagnostics = GetSortedDiagnostics(sources, language, analyzer, compilationOptions, parseOptions, validationMode, referenceFlags: referenceFlags, allowUnsafeCode: allowUnsafeCode);
            diagnostics.Verify(analyzer, PrintActualDiagnosticsOnFailure, ExpectedDiagnosticsAssertionTemplate, GetDefaultPath(language), expected);
        }

        protected static string GetDefaultPath(string language) =>
            language == LanguageNames.CSharp ? CSharpDefaultFilePath : VisualBasicDefaultFilePath;

        protected IEnumerable<TestAdditionalDocument> GetAdditionalTextFiles(string fileName, string text) =>
            ImmutableArray.Create(GetAdditionalTextFile(fileName, text));

        protected TestAdditionalDocument GetAdditionalTextFile(string fileName, string text) =>
            new TestAdditionalDocument(fileName, text);

        private static Tuple<Document[], bool, TextSpan?[]> GetDocumentsAndSpans(FileAndSource[] sources, string language, CompilationOptions compilationOptions, ParseOptions parseOptions, ReferenceFlags referenceFlags = ReferenceFlags.None, string projectName = TestProjectName, bool allowUnsafeCode = false)
        {
            Assert.True(language == LanguageNames.CSharp || language == LanguageNames.VisualBasic, "Unsupported language");

            var spans = new TextSpan?[sources.Length];
            bool useSpans = false;

            for (int i = 0; i < sources.Length; i++)
            {
                MarkupTestFile.GetPositionAndSpan(sources[i].Source, out string source, out _, out TextSpan? span);

                sources[i].Source = source;
                spans[i] = span;

                if (span != null)
                {
                    useSpans = true;
                }
            }

            Project project = CreateProject(sources, language, referenceFlags, null, projectName, allowUnsafeCode, compilationOptions: compilationOptions, parseOptions: parseOptions);
            Document[] documents = project.Documents.ToArray();
            Assert.Equal(sources.Length, documents.Length);

            return Tuple.Create(documents, useSpans, spans);
        }

        protected static Document CreateDocument(string source, string language = LanguageNames.CSharp, ReferenceFlags referenceFlags = ReferenceFlags.None, bool allowUnsafeCode = false)
        {
            return CreateProject(new[] { source }.ToFileAndSource(), language, referenceFlags, allowUnsafeCode: allowUnsafeCode).Documents.First();
        }

        protected static Document[] CreateDocuments(string[] sources, string language = LanguageNames.CSharp, bool allowUnsafeCode = false)
        {
            return CreateProject(sources.ToFileAndSource(), language, allowUnsafeCode: allowUnsafeCode).Documents.ToArray();
        }

        protected static Project CreateProject(string[] sources, string language = LanguageNames.CSharp, ReferenceFlags referenceFlags = ReferenceFlags.None, Solution addToSolution = null)
        {
            return CreateProject(sources.ToFileAndSource(), language, referenceFlags, addToSolution);
        }

        private static Project CreateProject(
            FileAndSource[] sources,
            string language = LanguageNames.CSharp,
            ReferenceFlags referenceFlags = ReferenceFlags.None,
            Solution addToSolution = null,
            string projectName = TestProjectName,
            bool allowUnsafeCode = false,
            CompilationOptions compilationOptions = null,
            ParseOptions parseOptions = null)
        {
            string fileNamePrefix = DefaultFilePathPrefix;
            string fileExt = language == LanguageNames.CSharp ? CSharpDefaultFileExt : VisualBasicDefaultExt;
            CompilationOptions options = compilationOptions ??
                (language == LanguageNames.CSharp
                    ? (allowUnsafeCode ? s_CSharpUnsafeCodeDefaultOptions : s_CSharpDefaultOptions) :
                      s_visualBasicDefaultOptions);

            ProjectId projectId = ProjectId.CreateNewId(debugName: projectName);

            Project project = (addToSolution ?? new AdhocWorkspace().CurrentSolution)
                .AddProject(projectId, projectName, projectName, language)
                .AddMetadataReference(projectId, MetadataReferences.CorlibReference)
                .AddMetadataReference(projectId, MetadataReferences.SystemCoreReference)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.SystemXmlReference)
                .AddMetadataReference(projectId, MetadataReferences.CodeAnalysisReference)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.SystemRuntimeFacadeRef)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.SystemThreadingFacadeRef)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.SystemThreadingTaskFacadeRef)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.WorkspacesReference)
                .AddMetadataReference(projectId, AdditionalMetadataReferences.SystemDiagnosticsDebugReference)
                .WithProjectCompilationOptions(projectId, options)
                .WithProjectParseOptions(projectId, parseOptions)
                .GetProject(projectId);

            if ((referenceFlags & ReferenceFlags.RemoveCodeAnalysis) != ReferenceFlags.RemoveCodeAnalysis)
            {
                MetadataReference symbolsReference = language == LanguageNames.CSharp ? AdditionalMetadataReferences.CSharpSymbolsReference : AdditionalMetadataReferences.VisualBasicSymbolsReference;
                project = project.AddMetadataReference(symbolsReference);
            }

            if ((referenceFlags & ReferenceFlags.RemoveImmutable) != ReferenceFlags.RemoveImmutable)
            {
                project = project.AddMetadataReference(MetadataReferences.SystemCollectionsImmutableReference);
            }

            if ((referenceFlags & ReferenceFlags.RemoveSystemData) != ReferenceFlags.RemoveSystemData)
            {
                project = project.AddMetadataReference(AdditionalMetadataReferences.SystemDataReference)
                    .AddMetadataReference(AdditionalMetadataReferences.SystemXmlDataReference);
            }

            if (language == LanguageNames.VisualBasic)
            {
                project = project.AddMetadataReference(MetadataReferences.MicrosoftVisualBasicReference);
            }

            int count = 0;
            foreach (FileAndSource source in sources)
            {
                string newFileName = source.FilePath ?? fileNamePrefix + count++ + "." + fileExt;
                DocumentId documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
                project = project.AddDocument(newFileName, SourceText.From(source.Source)).Project;
            }

            return project;
        }

        protected static Diagnostic[] GetSortedDiagnostics(FileAndSource[] sources, string language, DiagnosticAnalyzer analyzer, CompilationOptions compilationOptions, ParseOptions parseOptions, TestValidationMode validationMode = DefaultTestValidationMode, ReferenceFlags referenceFlags = ReferenceFlags.None, bool allowUnsafeCode = false, string projectName = TestProjectName, IEnumerable<TestAdditionalDocument> additionalFiles = null)
        {
            Tuple<Document[], bool, TextSpan?[]> documentsAndUseSpan = GetDocumentsAndSpans(sources, language, compilationOptions, parseOptions, referenceFlags, projectName, allowUnsafeCode);
            Document[] documents = documentsAndUseSpan.Item1;
            bool useSpans = documentsAndUseSpan.Item2;
            TextSpan?[] spans = documentsAndUseSpan.Item3;
            return GetSortedDiagnostics(analyzer, documents, useSpans ? spans : null, validationMode, additionalFiles);
        }

        protected static Diagnostic[] GetSortedDiagnostics(DiagnosticAnalyzer analyzerOpt, Document document, TextSpan?[] spans = null, IEnumerable<TestAdditionalDocument> additionalFiles = null)
        {
            return GetSortedDiagnostics(analyzerOpt, new[] { document }, spans, additionalFiles: additionalFiles);
        }

        protected static Diagnostic[] GetSortedDiagnostics(DiagnosticAnalyzer analyzerOpt, Document[] documents, TextSpan?[] spans = null, TestValidationMode validationMode = DefaultTestValidationMode, IEnumerable<TestAdditionalDocument> additionalFiles = null)
        {
            if (analyzerOpt == null)
            {
                return Array.Empty<Diagnostic>();
            }

            var projects = new HashSet<Project>();
            foreach (Document document in documents)
            {
                projects.Add(document.Project);
            }

            var analyzerOptions = additionalFiles != null ? new AnalyzerOptions(additionalFiles.ToImmutableArray<AdditionalText>()) : null;
            DiagnosticBag diagnostics = DiagnosticBag.GetInstance();
            foreach (Project project in projects)
            {
                Compilation compilation = project.GetCompilationAsync().Result;
                compilation = EnableAnalyzer(analyzerOpt, compilation);

                ImmutableArray<Diagnostic> diags = compilation.GetAnalyzerDiagnostics(new[] { analyzerOpt }, validationMode, analyzerOptions);
                if (spans == null)
                {
                    diagnostics.AddRange(diags);
                }
                else
                {
                    Debug.Assert(spans.Length == documents.Length);
                    foreach (Diagnostic diag in diags)
                    {
                        if (diag.Location == Location.None || diag.Location.IsInMetadata)
                        {
                            diagnostics.Add(diag);
                        }
                        else
                        {
                            for (int i = 0; i < documents.Length; i++)
                            {
                                Document document = documents[i];
                                SyntaxTree tree = document.GetSyntaxTreeAsync().Result;
                                if (tree == diag.Location.SourceTree)
                                {
                                    TextSpan? span = spans[i];
                                    if (span == null || span.Value.Contains(diag.Location.SourceSpan))
                                    {
                                        diagnostics.Add(diag);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Diagnostic[] results = diagnostics.AsEnumerable().OrderBy(d => d.Location.SourceSpan.Start).ToArray();
            diagnostics.Free();
            return results;
        }

        private static Compilation EnableAnalyzer(DiagnosticAnalyzer analyzer, Compilation compilation)
        {
            return compilation.WithOptions(
                compilation
                    .Options
                    .WithSpecificDiagnosticOptions(
                        analyzer
                            .SupportedDiagnostics
                            .Select(x => KeyValuePair.Create(x.Id, ReportDiagnostic.Default))
                            .ToImmutableDictionaryOrEmpty()));
        }

        protected static FileAndSource GetEditorConfigAdditionalFile(string source)
            => new FileAndSource() { Source = source, FilePath = ".editorconfig" };
    }

    // Justification for suppression: We are not going to compare FileAndSource objects for equality.
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct FileAndSource
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public string FilePath { get; set; }
        public string Source { get; set; }
    }
}
