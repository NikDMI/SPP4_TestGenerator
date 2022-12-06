using System;
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
            _rootTestDirectory = resultDirectoryPath;
            ExecutionDataflowBlockOptions actionOptions = new ExecutionDataflowBlockOptions();
            actionOptions.MaxDegreeOfParallelism = 3;
            _generatorAction = new ActionBlock<string>(ParseClassFile, actionOptions);
            foreach(var classFileName in classFileNames)
            {
                _generatorAction.Post(classFileName);
            }
            //Create task to return to the user
            Task userTask = new Task(() => { _generatorAction.Complete(); _generatorAction.Completion.Wait(); });
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
                    await CreateTestFile(childNode, sourceFileName);
                } else if (childNode.IsKind(SyntaxKind.NamespaceDeclaration))
                {
                    await ProcessUserClassesFromSyntaxNode(sourceFileName, childNode);
                }
            }
        }


        private async Task CreateTestFile(SyntaxNode classNode, string fileName)
        {

        }


        private ActionBlock<string> _generatorAction;
        private string _rootTestDirectory;
    }
}
