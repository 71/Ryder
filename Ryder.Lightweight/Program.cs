using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Ryder.Lightweight
{
    /// <summary>
    ///   Program used to create the 'Ryder.Lightweight.cs' file.
    /// </summary>
    internal static class Program
    {
        private const string OUTPUT_FILENAME = "Ryder.Lightweight.cs";

        /// <summary>
        ///   Entry point of the program: generates the 'Ryder.Lightweight.cs' file.
        /// </summary>
        public static int Main(string[] args)
        {
            string ns = null;
            string dir = Directory.GetCurrentDirectory();
            string output = Path.Combine(Directory.GetCurrentDirectory(), OUTPUT_FILENAME);
            bool makePublic = false;

#if DEBUG
            if (dir.EndsWith(nameof(Lightweight), StringComparison.OrdinalIgnoreCase))
            {
                dir = Path.Combine(dir, ".." /* Ryder */, "Ryder");
            }
            else
            {
                output = Path.Combine(dir /* Debug */, ".." /* bin */, ".." /* Ryder.Lightweight */);
                dir = Path.Combine(output, ".." /* Ryder */, "Ryder");
                output = Path.Combine(output, OUTPUT_FILENAME);
            }

            dir = Path.GetFullPath(dir);
            output = Path.GetFullPath(output);
            ns = "Ryder.Lightweight";
#endif

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--public":
                    case "-p":
                        makePublic = true;
                        break;
                    case "--namespace":
                    case "-n":
                        if (args.Length == i + 1)
                        {
                            Console.Error.WriteLine("No namespace given.");
                            return 1;
                        }

                        ns = args[++i];
                        break;
                    case "--directory":
                    case "-d":
                        if (args.Length == i + 1)
                        {
                            Console.Error.WriteLine("No directory given.");
                            return 1;
                        }

                        dir = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (args.Length == i + 1)
                        {
                            Console.Error.WriteLine("No directory given.");
                            return 1;
                        }

                        output = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: '{args[i]}'.");
                        return 1;
                }
            }

            string methodRedirectionPath = Path.Combine(dir, "Redirection.Method.cs");
            string helpersPath = Path.Combine(dir, "Helpers.cs");

            if (!File.Exists(methodRedirectionPath) || !File.Exists(helpersPath))
            {
                Console.Error.WriteLine("Invalid directory given.");
                return 1;
            }

            try
            {
                // Read files
                string methodRedirectionContent = File.ReadAllText(methodRedirectionPath);
                string helpersContent = File.ReadAllText(helpersPath);

                // Parse content to trees, and get their root / classes / usings
                SyntaxTree methodRedirectionTree = SyntaxFactory.ParseSyntaxTree(methodRedirectionContent, path: methodRedirectionPath);
                SyntaxTree helpersTree = SyntaxFactory.ParseSyntaxTree(helpersContent, path: helpersPath);

                CompilationUnitSyntax methodRedirection = methodRedirectionTree.GetCompilationUnitRoot();
                CompilationUnitSyntax helpers = helpersTree.GetCompilationUnitRoot();

                UsingDirectiveSyntax[] usings = methodRedirection.Usings.Select(x => x.Name.ToString())
                    .Concat(helpers.Usings.Select(x => x.Name.ToString()))
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(x)))
                    .ToArray();

                ClassDeclarationSyntax methodRedirectionClass = methodRedirection.DescendantNodes()
                                                                                 .OfType<ClassDeclarationSyntax>()
                                                                                 .First();
                ClassDeclarationSyntax helpersClass = helpers.DescendantNodes()
                                                             .OfType<ClassDeclarationSyntax>()
                                                             .First();

                // Set visibility of main class
                if (!makePublic)
                {
                    var modifiers = methodRedirectionClass.Modifiers;
                    var publicModifier = modifiers.First(x => x.Kind() == SyntaxKind.PublicKeyword);

                    methodRedirectionClass = methodRedirectionClass.WithModifiers(
                        modifiers.Replace(publicModifier, SyntaxFactory.Token(SyntaxKind.InternalKeyword))
                    );
                }

                // Set visibility of helpers class
                helpersClass = helpersClass.WithModifiers(
                    helpersClass.Modifiers.Replace(
                        helpersClass.Modifiers.First(x => x.Kind() == SyntaxKind.InternalKeyword),
                        SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
                    )
                );

                // Change helpers class extension methods to normal methods
                var extMethods = helpersClass.DescendantNodes()
                                             .OfType<MethodDeclarationSyntax>()
                                             .Where(x => x.ParameterList.DescendantTokens().Any(tok => tok.Kind() == SyntaxKind.ThisKeyword));
                var extMethodsNames = extMethods.Select(x => x.Identifier.Text);

                helpersClass = helpersClass.ReplaceNodes(
                    helpersClass.DescendantNodes().OfType<ParameterSyntax>().Where(x => x.Modifiers.Any(SyntaxKind.ThisKeyword)),
                    (x,_) => x.WithModifiers(x.Modifiers.Remove(x.Modifiers.First(y => y.Kind() == SyntaxKind.ThisKeyword)))
                );

                // Disable overrides
                var members = methodRedirectionClass.Members;

                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];

                    if (!(member is MethodDeclarationSyntax method))
                    {
                        if (member is ConstructorDeclarationSyntax ctor)
                            members = members.Replace(ctor, ctor.WithIdentifier(SyntaxFactory.Identifier("Redirection")));

                        continue;
                    }

                    var overrideModifier = method.Modifiers.FirstOrDefault(x => x.Kind() == SyntaxKind.OverrideKeyword);

                    if (overrideModifier == default(SyntaxToken))
                        continue;

                    method = method.WithModifiers(
                        method.Modifiers.Remove(overrideModifier)
                    );

                    members = members.Replace(member, method);
                }

                // Add missing field
                var field = SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                        SyntaxFactory.SeparatedList(new[] {
                            SyntaxFactory.VariableDeclarator("isRedirecting")
                        })
                    )
                );

                // Add docs
                const string DOCS = @"
/// <summary>
///   Provides the ability to redirect calls from one method to another.
/// </summary>
";

                methodRedirectionClass = methodRedirectionClass.WithMembers(members)
                    // Add docs
                    .WithLeadingTrivia(SyntaxFactory.Comment(DOCS))
                    // Rename to 'Redirection'
                    .WithIdentifier(SyntaxFactory.Identifier("Redirection"))
                    // Disable inheritance
                    .WithBaseList(null)
                    // Embed helpers, missing field
                    .AddMembers(field, helpersClass);

                // Generate namespace (or member, if no namespace is specified)
                MemberDeclarationSyntax @namespace = ns == null
                    ? (MemberDeclarationSyntax)methodRedirectionClass
                    : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(ns)).AddMembers(methodRedirectionClass);

                var extCalls = @namespace.DescendantNodes()
                                         .OfType<InvocationExpressionSyntax>()
                                         .Where(x => x.Expression is MemberAccessExpressionSyntax access && extMethodsNames.Contains(access.Name.Identifier.Text));
                var helpersAccess = SyntaxFactory.IdentifierName("Helpers");

                @namespace = @namespace.ReplaceNodes(
                    extCalls,
                    (x, _) => SyntaxFactory.InvocationExpression(((MemberAccessExpressionSyntax)x.Expression).WithExpression(helpersAccess)).WithArgumentList(x.ArgumentList.WithArguments(x.ArgumentList.Arguments.Insert(0, SyntaxFactory.Argument(((MemberAccessExpressionSyntax)x.Expression).Expression)))));

                // Generate syntax root
                CompilationUnitSyntax root = SyntaxFactory.CompilationUnit()
                                                          .AddUsings(usings)
                                                          .AddMembers(@namespace);

                // Print root to file
                using (FileStream fs = File.OpenWrite(output))
                using (TextWriter writer = new StreamWriter(fs))
                {
                    fs.SetLength(0);

                    Formatter.Format(root, new AdhocWorkspace()).WriteTo(writer);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error encountered:");
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            return 0;
        }
    }
}