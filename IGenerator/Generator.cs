﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Threading.Tasks.Dataflow;
using System.IO;
using System.Text;

namespace TestGenerator.IGenerator
{
    public class Generator : ITestGenerator
    {
        public Task GetTestFiles(List<string> classFileNames, string resultDirectoryPath)
        {
            if (!Directory.Exists(resultDirectoryPath))
            {
                throw new ArgumentException ("Directory not founded");
            }
            _rootTestDirectory = resultDirectoryPath;
            //Create actions
            ExecutionDataflowBlockOptions actionOptions = new ExecutionDataflowBlockOptions();
            actionOptions.MaxDegreeOfParallelism = 3;
            _generatorAction = new ActionBlock<string>(ParseClassFile, actionOptions);
            _testGeneratorAction = new ActionBlock<SyntaxNode>(CreateTestFile, actionOptions);
            _testSavingAction = new ActionBlock<string>(WriteTestToFile, actionOptions);
            foreach (var classFileName in classFileNames)
            {
                _generatorAction.Post(classFileName);
            }
            //Create task to return to the user
            Task userTask = new Task(() =>
            {
                _generatorAction.Complete();//Parse class files
                _generatorAction.Completion.Wait();
                _testGeneratorAction.Complete();//Generate test classes
                _testGeneratorAction.Completion.Wait();
                _testSavingAction.Complete();//Write test classes to files
                _testSavingAction.Completion.Wait();
            });
            return userTask;
        }


        /*
         * Create syntax tree from source code file
         */
        private async void ParseClassFile(string fileName)
        {
            //Read data in the file
            byte[] fileData = null;
            using (FileStream sourceStream = File.Open(fileName, FileMode.Open))
            {
                fileData = new byte[sourceStream.Length];
                await sourceStream.ReadAsync(fileData, 0, fileData.Length);
            }
            string sourceFileContent = Encoding.UTF8.GetString(fileData);
            SyntaxTree syntaxTree = SyntaxFactory.ParseSyntaxTree(sourceFileContent, null, "", UTF8Encoding.UTF8);
            var rootNode = await syntaxTree.GetRootAsync();
            await ProcessUserClassesFromSyntaxNode(fileName, rootNode);
        }


        /*
         * Finds all user classes and generates test files
         */
        private async Task ProcessUserClassesFromSyntaxNode(string sourceFileName, SyntaxNode syntaxNode)
        {
            //Find all class declarations
            foreach (var childNode in syntaxNode.ChildNodes())
            {
                if (childNode.IsKind(SyntaxKind.ClassDeclaration))
                {
                    _testGeneratorAction.Post(childNode);

                }
                else if (childNode.IsKind(SyntaxKind.NamespaceDeclaration))
                {
                    await ProcessUserClassesFromSyntaxNode(sourceFileName, childNode);
                }
            }
        }


        private async Task CreateTestFile(SyntaxNode classNode)
        {
            ClassDeclarationSyntax classDeclarationSyntax = (ClassDeclarationSyntax)classNode;
            var className = classDeclarationSyntax.Identifier.Text;
            //Create new compilation unit
            List<SyntaxNode> publicMethods = new List<SyntaxNode>();
            foreach (var child in classNode.ChildNodes())
            {
                if (child.IsKind(SyntaxKind.MethodDeclaration))
                {
                    MethodDeclarationSyntax methodDeclarationSyntax = (MethodDeclarationSyntax)child;
                    if (methodDeclarationSyntax.Modifiers.Any(SyntaxKind.PublicKeyword))
                    {
                        publicMethods.Add(child);
                    }
                }

            }
            //Get namespace of the class
            SyntaxNode outerNamespace;
            do
            {
                outerNamespace = classNode.Parent;
            } while (!outerNamespace.IsKind(SyntaxKind.NamespaceDeclaration));
            var testCompilationUnit = GetCompilationTestFile(className, outerNamespace, publicMethods);
            string testFileText = testCompilationUnit.ToFullString();
            _testSavingAction.Post(testFileText);
        }


        /*
         * Create source file for test unit
         */
        private CompilationUnitSyntax GetCompilationTestFile(string className, SyntaxNode nodeNamespace, List<SyntaxNode> publicMethods)
        {
            NamespaceDeclarationSyntax namespaceDeclarationSyntax = (NamespaceDeclarationSyntax)nodeNamespace;
            var compilationUnit = SyntaxFactory.CompilationUnit();
            compilationUnit = compilationUnit
            .WithUsings(
                SyntaxFactory.List<UsingDirectiveSyntax>(
                    new UsingDirectiveSyntax[]{
                SyntaxFactory.UsingDirective(
                    SyntaxFactory.IdentifierName("System")),
                SyntaxFactory.UsingDirective(
                        SyntaxFactory.QualifiedName(
                        SyntaxFactory.QualifiedName(
                        SyntaxFactory.IdentifierName("System"),
                        SyntaxFactory.IdentifierName("Collections")),
                    SyntaxFactory.IdentifierName("Generic"))),
                SyntaxFactory.UsingDirective(
                    SyntaxFactory.QualifiedName(
                        SyntaxFactory. IdentifierName("System"),
                    SyntaxFactory.IdentifierName("Linq"))),
                SyntaxFactory.UsingDirective(
                    SyntaxFactory.QualifiedName(
                        SyntaxFactory.IdentifierName("System"),
                        SyntaxFactory.IdentifierName("Text"))),
                SyntaxFactory.UsingDirective(
                    SyntaxFactory.QualifiedName(
                        SyntaxFactory.IdentifierName("NUnit"),
                        SyntaxFactory.IdentifierName("Framework"))),
                SyntaxFactory.UsingDirective(
                    namespaceDeclarationSyntax.Name)}))
            .WithMembers(
                SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                SyntaxFactory.NamespaceDeclaration(
                    SyntaxFactory.QualifiedName(
                        namespaceDeclarationSyntax.Name,
                        SyntaxFactory.IdentifierName("Tests")))
            .WithMembers(
            SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                SyntaxFactory.ClassDeclaration(className)
                .WithAttributeLists(
                   SyntaxFactory.SingletonList<AttributeListSyntax>(
                        SyntaxFactory.AttributeList(
                            SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                                SyntaxFactory.Attribute(
                                    SyntaxFactory.IdentifierName("Test"))))))
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithMembers(
                    SyntaxFactory.List<MemberDeclarationSyntax>(GetMethodDeclarations(publicMethods)))))))
                    .NormalizeWhitespace();
            return compilationUnit;
        }


        /*
         * Creates array of method declarations for test
         */
        MemberDeclarationSyntax[] GetMethodDeclarations(List<SyntaxNode> methods)
        {
            List<MemberDeclarationSyntax> methodsDeclarations = new List<MemberDeclarationSyntax>();
            foreach (var methodNode in methods)
            {
                MethodDeclarationSyntax methodDeclarationSyntax = (MethodDeclarationSyntax)methodNode;
                var methodDeclaration = SyntaxFactory.MethodDeclaration(
                                SyntaxFactory.PredefinedType(
                                    SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                                SyntaxFactory.Identifier(methodDeclarationSyntax.Identifier.Text))
                            .WithAttributeLists(
                                SyntaxFactory.SingletonList<AttributeListSyntax>(
                                    SyntaxFactory.AttributeList(
                                        SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                                            SyntaxFactory.Attribute(
                                                SyntaxFactory.IdentifierName("Test"))))))
                            .WithModifiers(
                                SyntaxFactory.TokenList(
                                    SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                            .WithBody(
                                SyntaxFactory.Block(
                                    SyntaxFactory.SingletonList<StatementSyntax>(
                                        SyntaxFactory.ExpressionStatement(
                                            SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName("Assert"),
                                                    SyntaxFactory.IdentifierName("Fail")))
                                            .WithArgumentList(
                                                SyntaxFactory.ArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList<ArgumentSyntax>(
                                                        SyntaxFactory.Argument(
                                                            SyntaxFactory.LiteralExpression(
                                                                SyntaxKind.StringLiteralExpression,
                                                               SyntaxFactory.Literal("autogenerated"))))))))));
                methodsDeclarations.Add(methodDeclaration);
            }
            return methodsDeclarations.ToArray();
        }


        private async void WriteTestToFile(string fileData)
        {
            byte[] data = Encoding.UTF8.GetBytes(fileData);
            string filePath = "";
            lock (_rootTestDirectory) 
            {
                filePath = _rootTestDirectory + _currentTestFileId++ + ".cs";
            }
            using (FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate))
            {
                await fs.WriteAsync(data, 0, data.Length);
            }
        }


        private ActionBlock<string> _generatorAction;
        private ActionBlock<SyntaxNode> _testGeneratorAction;
        private ActionBlock<string> _testSavingAction;
        private string _rootTestDirectory;
        private int _currentTestFileId = 1;
    }
}
