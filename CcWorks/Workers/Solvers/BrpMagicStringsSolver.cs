using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Text;

namespace CcWorks.Workers.Solvers
{
    public static class BrpMagicStringsSolver
    {
        public static async Task<string> Solve(string text)
        {
            using (var workspace = new AdhocWorkspace())
            {
                var projectInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(), 
                    VersionStamp.Create(), 
                    "NewProject", 
                    "projName", 
                    LanguageNames.CSharp);

                var newProject = workspace.AddProject(projectInfo);
                var document = workspace.AddDocument(newProject.Id, "NewFile.cs", SourceText.From(text));
                var syntaxRoot = await document.GetSyntaxRootAsync();
                var editor = await DocumentEditor.CreateAsync(document);
                
                foreach (var classNode in syntaxRoot.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var existingConstants = AnalyzeExistingConstants(classNode);
                    var constantsFromCode = AnalyzeMagicStrings(classNode);

                    var constantsToCreate = constantsFromCode
                        .Where(c => !existingConstants.ContainsKey(c))
                        .ToList();

                    var newConstants = CreateConstants(constantsToCreate, classNode, editor);

                    var allConstants = existingConstants
                        .Concat(newConstants)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    ReplaceMagicStrings(allConstants, classNode, editor);
                }

                var newDocument = editor.GetChangedDocument();
                return (await newDocument.GetTextAsync()).ToString();
            }
        }

        private static Dictionary<string, string> AnalyzeExistingConstants(ClassDeclarationSyntax classNode)
        {
            var result = new Dictionary<string, string>();

            var constantFields = classNode.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(t => t.Kind() == SyntaxKind.ConstKeyword))
                .SelectMany(f => f.Declaration.Variables);

            foreach (var constant in constantFields)
            {
                var stringValue = constant.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .SingleOrDefault(s => s.Kind() == SyntaxKind.StringLiteralExpression);

                if (stringValue == null)
                {
                    continue;
                }

                var constantName = constant.Identifier.Text;
                var constantValue = stringValue.GetText().ToString();

                result.Add(constantValue, constantName);
            }

            return result;
        }

        private static List<string> AnalyzeMagicStrings(SyntaxNode node)
        {
            var counts = new Dictionary<string, int>();

            foreach (var child in node.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(s => s.Kind() == SyntaxKind.StringLiteralExpression))
            {
                var value = child.WithoutTrivia().GetText().ToString();
                if (value == "\"\"")
                {
                    continue;
                }

                if (counts.TryGetValue(value, out var count))
                {
                    counts[value] = count + 1;
                }
                else
                {
                    counts.Add(value, 1);
                }
            }

            return counts
                .Where(kv => kv.Value > 1)
                .Select(kv => kv.Key)
                .ToList();
        }

        private static Dictionary<string, string> CreateConstants(
            IReadOnlyCollection<string> constantsToCreate, 
            ClassDeclarationSyntax classNode,
            DocumentEditor editor)
        {
            var result = new Dictionary<string, string>();

            if (!constantsToCreate.Any())
            {
                return result;
            }

            var trivia = classNode.GetLeadingTrivia().ToString();
            trivia = trivia.Substring(trivia.LastIndexOf('\n') + 1);

            string indent;
            if (string.IsNullOrEmpty(trivia))
            {
                indent = "    ";
            }
            else
            {
                indent = trivia + trivia;
            }

            var counter = 0;
            var newConstants = new List<MemberDeclarationSyntax>();
            foreach (var constant in constantsToCreate)
            {
                counter++;

                var constantSubstring = constant.Substring(1, constant.Length - 2);
                var constName = GetConstantName(constantSubstring, counter);
                var constValue = constantSubstring;

                var constSyntax = SyntaxFactory.FieldDeclaration(
                        SyntaxFactory.VariableDeclaration(
                                SyntaxFactory.PredefinedType(
                                    SyntaxFactory.Token(SyntaxKind.StringKeyword)))
                            .WithVariables(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.VariableDeclarator(
                                            SyntaxFactory.Identifier(constName))
                                        .WithInitializer(
                                            SyntaxFactory.EqualsValueClause(
                                                SyntaxFactory.LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    SyntaxFactory.Literal(constValue)))))))
                    .WithModifiers(
                        SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                            SyntaxFactory.Token(SyntaxKind.ConstKeyword)))
                    .NormalizeWhitespace()
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(indent))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                
                newConstants.Add(constSyntax);
                result.Add(constant, constName);
            }

            editor.InsertMembers(classNode, 0, newConstants);
            return result;
        }

        private static string GetConstantName(string constantSubstring, int counter)
        {
            var nonLeadingNumbers = Regex.Replace(constantSubstring, @"(^\d+$)|(^\d+(?=\w))", string.Empty);
            var nonSpace = nonLeadingNumbers.Replace(" ", string.Empty);
            var trimmed = Regex.Replace(nonSpace, @"[^\w]", string.Empty);

            string constName;
            if (trimmed.Any())
            {
                constName = trimmed.Substring(0, 1).ToUpper();
                constName = constName
                    + (trimmed.Length > 1
                        ? trimmed.Substring(1, trimmed.Length - 1)
                        : string.Empty);
            }
            else
            {
                constName = "C" + counter;
            }

            return constName;
        }

        private static void ReplaceMagicStrings(
            Dictionary<string, string> constants, 
            SyntaxNode classNode, 
            DocumentEditor editor)
        {
            foreach (var child in classNode.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(s => s.Kind() == SyntaxKind.StringLiteralExpression))
            {
                var value = child.WithoutTrivia().GetText().ToString();
                if (value == "\"\"")
                {
                    if (IsIgnoredEmptyStringNode(child))
                    {
                        continue;
                    }

                    var stringEmpty = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                        SyntaxFactory.IdentifierName("Empty"));

                    if (child.HasLeadingTrivia)
                    {
                        stringEmpty = stringEmpty.WithLeadingTrivia(child.GetLeadingTrivia());
                    }

                    if (child.HasTrailingTrivia)
                    {
                        stringEmpty = stringEmpty.WithTrailingTrivia(child.GetTrailingTrivia());
                    }

                    editor.ReplaceNode(child, stringEmpty);
                }

                if (constants.TryGetValue(value, out var name))
                {
                    if (IsNodeConst(child))
                    {
                        continue;
                    }

                    var useConstant = SyntaxFactory.IdentifierName(name);

                    if (child.HasLeadingTrivia)
                    {
                        useConstant = useConstant.WithLeadingTrivia(child.GetLeadingTrivia());
                    }

                    if (child.HasTrailingTrivia)
                    {
                        useConstant = useConstant.WithTrailingTrivia(child.GetTrailingTrivia());
                    }

                    editor.ReplaceNode(child, useConstant);
                }
            }
        }

        private static bool IsIgnoredEmptyStringNode(SyntaxNode node)
        {
            var parent = node;
            while (true)
            {
                if (parent == null || parent is ClassDeclarationSyntax)
                {
                    return false;
                }

                if (parent is FieldDeclarationSyntax field)
                {
                    return field.Modifiers.Any(t => t.Kind() == SyntaxKind.ConstKeyword);
                }

                if (parent is ParameterSyntax)
                {
                    return true;
                }

                if (parent is LocalDeclarationStatementSyntax declaration)
                {
                    return declaration.Modifiers.Any(t => t.Kind() == SyntaxKind.ConstKeyword);
                }

                if (parent is AttributeArgumentSyntax)
                {
                    return true;
                }

                parent = parent.Parent;
            }
        }

        private static bool IsNodeConst(SyntaxNode node)
        {
            var parent = node;
            while (true)
            {
                if (parent == null || parent is ClassDeclarationSyntax)
                {
                    return false;
                }

                if (parent is FieldDeclarationSyntax field)
                {
                    return field.Modifiers.Any(t => t.Kind() == SyntaxKind.ConstKeyword);
                }

                parent = parent.Parent;
            }
        }
    }
}