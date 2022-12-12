using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace TestGenerator.IGenerator
{
    public interface ITestGenerator
    {
        /*
         * Generates test files
         * classFileNames - files, that contains definitions of user classes
         * resultDirectoryPath - directory to store test files
         */
        public Task GetTestFiles(List<string> classFileNames, string resultDirectoryPath, int loadMaxThreads,
            int parsingMaxThreads, int savingMaxThreads);
    }
}
