using Cecilia.Cil;
using Cecilia.PE;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;

namespace Cecilia.Tests
{

    public abstract class BaseTestFixture
    {

        protected static void IgnoreOnMono()
        {
            if (Platform.OnMono)
                Assert.Ignore();
        }

        protected static void IgnoreOnCoreClr()
        {
            if (Platform.OnCoreClr)
                Assert.Ignore();
        }

        protected static void OnlyOnWindows()
        {
            if (!Platform.OnWindows)
                Assert.Ignore();
        }

        public static string GetResourcePath(string name)
        {
            return Path.Combine(ResourcesDirectory, name);
        }

        public static string GetAssemblyResourcePath(string name)
        {
            return GetResourcePath(Path.Combine("assemblies", name));
        }

        public static string GetCSharpResourcePath(string name)
        {
            return GetResourcePath(Path.Combine("cs", name));
        }

        public static string GetILResourcePath(string name)
        {
            return GetResourcePath(Path.Combine("il", name));
        }

        public ModuleDefinition GetResourceModule(string name)
        {
            return ModuleDefinition.ReadModule(GetAssemblyResourcePath(name));
        }

        public ModuleDefinition GetResourceModule(string name, ReaderParameters parameters)
        {
            return ModuleDefinition.ReadModule(GetAssemblyResourcePath(name), parameters);
        }

        public ModuleDefinition GetResourceModule(string name, ReadingMode mode)
        {
            return ModuleDefinition.ReadModule(GetAssemblyResourcePath(name), new ReaderParameters(mode));
        }

        public Stream GetResourceStream(string name)
        {
            return new FileStream(GetAssemblyResourcePath(name), FileMode.Open, FileAccess.Read);
        }

        internal Image GetResourceImage(string name)
        {
            var file = new FileStream(GetAssemblyResourcePath(name), FileMode.Open, FileAccess.Read);
            return ImageReader.ReadImage(Disposable.Owned(file as Stream), file.Name);
        }

        public ModuleDefinition GetCurrentModule()
        {
            return ModuleDefinition.ReadModule(GetType().Module.FullyQualifiedName);
        }

        public ModuleDefinition GetCurrentModule(ReaderParameters parameters)
        {
            return ModuleDefinition.ReadModule(GetType().Module.FullyQualifiedName, parameters);
        }

        public static readonly string ResourcesDirectory =
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources");

        public static void AssertCode(string expected, MethodDefinition method)
        {
            Assert.IsTrue(method.HasBody);
            Assert.IsNotNull(method.Body);

            Assert.AreEqual(Normalize(expected), Normalize(Formatter.FormatMethodBody(method)));
        }

        public static string Normalize(string str)
        {
            return str.Trim().Replace("\r\n", "\n");
        }

        public static void TestModule(string file, Action<ModuleDefinition> test, bool verify = true, bool readOnly = false, Type symbolReaderProvider = null, Type symbolWriterProvider = null, IAssemblyResolver assemblyResolver = null, bool applyWindowsRuntimeProjections = false)
        {
            Run(new ModuleTestCase(file, test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections));
        }

        public static void TestCSharp(string file, Action<ModuleDefinition> test, bool verify = true, bool readOnly = false, Type symbolReaderProvider = null, Type symbolWriterProvider = null, IAssemblyResolver assemblyResolver = null, bool applyWindowsRuntimeProjections = false)
        {
            Run(new CSharpTestCase(file, test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections));
        }

        public static void TestIL(string file, Action<ModuleDefinition> test, bool verify = true, bool readOnly = false, Type symbolReaderProvider = null, Type symbolWriterProvider = null, IAssemblyResolver assemblyResolver = null, bool applyWindowsRuntimeProjections = false)
        {
            Run(new ILTestCase(file, test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections));
        }

        static void Run(TestCase testCase)
        {
            using (var runner = new TestRunner(testCase, TestCaseType.ReadDeferred))
                runner.RunTest();

            using (var runner = new TestRunner(testCase, TestCaseType.ReadImmediate))
                runner.RunTest();

            if (testCase.ReadOnly)
                return;

            using (var runner = new TestRunner(testCase, TestCaseType.WriteFromDeferred))
                runner.RunTest();

            using (var runner = new TestRunner(testCase, TestCaseType.WriteFromImmediate))
                runner.RunTest();
        }
    }

    abstract class TestCase
    {

        public readonly bool Verify;
        public readonly bool ReadOnly;
        public readonly Type SymbolReaderProvider;
        public readonly Type SymbolWriterProvider;
        public readonly IAssemblyResolver AssemblyResolver;
        public readonly Action<ModuleDefinition> Test;
        public readonly bool ApplyWindowsRuntimeProjections;

        public abstract string ModuleLocation { get; }

        protected TestCase(Action<ModuleDefinition> test, bool verify, bool readOnly, Type symbolReaderProvider, Type symbolWriterProvider, IAssemblyResolver assemblyResolver, bool applyWindowsRuntimeProjections)
        {
            Test = test;
            Verify = verify;
            ReadOnly = readOnly;
            SymbolReaderProvider = symbolReaderProvider;
            SymbolWriterProvider = symbolWriterProvider;
            AssemblyResolver = assemblyResolver;
            ApplyWindowsRuntimeProjections = applyWindowsRuntimeProjections;
        }
    }

    class ModuleTestCase : TestCase
    {

        public readonly string Module;

        public ModuleTestCase(string module, Action<ModuleDefinition> test, bool verify, bool readOnly, Type symbolReaderProvider, Type symbolWriterProvider, IAssemblyResolver assemblyResolver, bool applyWindowsRuntimeProjections)
            : base(test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections)
        {
            Module = module;
        }

        public override string ModuleLocation
        {
            get { return BaseTestFixture.GetAssemblyResourcePath(Module); }
        }
    }

    class CSharpTestCase : TestCase
    {

        public readonly string File;

        public CSharpTestCase(string file, Action<ModuleDefinition> test, bool verify, bool readOnly, Type symbolReaderProvider, Type symbolWriterProvider, IAssemblyResolver assemblyResolver, bool applyWindowsRuntimeProjections)
            : base(test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections)
        {
            File = file;
        }

        public override string ModuleLocation
        {
            get
            {
                return CompilationService.CompileResource(BaseTestFixture.GetCSharpResourcePath(File));
            }
        }
    }

    class ILTestCase : TestCase
    {

        public readonly string File;

        public ILTestCase(string file, Action<ModuleDefinition> test, bool verify, bool readOnly, Type symbolReaderProvider, Type symbolWriterProvider, IAssemblyResolver assemblyResolver, bool applyWindowsRuntimeProjections)
            : base(test, verify, readOnly, symbolReaderProvider, symbolWriterProvider, assemblyResolver, applyWindowsRuntimeProjections)
        {
            File = file;
        }

        public override string ModuleLocation
        {
            get
            {
                return CompilationService.CompileResource(BaseTestFixture.GetILResourcePath(File)); ;
            }
        }
    }

    class TestRunner : IDisposable
    {

        readonly TestCase test_case;
        readonly TestCaseType type;

        ModuleDefinition test_module;
        DefaultAssemblyResolver test_resolver;

        public TestRunner(TestCase testCase, TestCaseType type)
        {
            this.test_case = testCase;
            this.type = type;
        }

        ModuleDefinition GetModule()
        {
            var location = test_case.ModuleLocation;

            var parameters = new ReaderParameters
            {
                SymbolReaderProvider = GetSymbolReaderProvider(),
                AssemblyResolver = GetAssemblyResolver(),
                ApplyWindowsRuntimeProjections = test_case.ApplyWindowsRuntimeProjections
            };

            switch (type)
            {
                case TestCaseType.ReadImmediate:
                    parameters.ReadingMode = ReadingMode.Immediate;
                    return ModuleDefinition.ReadModule(location, parameters);
                case TestCaseType.ReadDeferred:
                    parameters.ReadingMode = ReadingMode.Deferred;
                    return ModuleDefinition.ReadModule(location, parameters);
                case TestCaseType.WriteFromImmediate:
                    parameters.ReadingMode = ReadingMode.Immediate;
                    return RoundTrip(location, parameters, "cecil-irt");
                case TestCaseType.WriteFromDeferred:
                    parameters.ReadingMode = ReadingMode.Deferred;
                    return RoundTrip(location, parameters, "cecil-drt");
                default:
                    return null;
            }
        }

        ISymbolReaderProvider GetSymbolReaderProvider()
        {
            if (test_case.SymbolReaderProvider == null)
                return null;

            return (ISymbolReaderProvider)Activator.CreateInstance(test_case.SymbolReaderProvider);
        }

        ISymbolWriterProvider GetSymbolWriterProvider()
        {
            if (test_case.SymbolReaderProvider == null)
                return null;

            return (ISymbolWriterProvider)Activator.CreateInstance(test_case.SymbolWriterProvider);
        }

        IAssemblyResolver GetAssemblyResolver()
        {
            if (test_case.AssemblyResolver != null)
                return test_case.AssemblyResolver;

            test_resolver = new DefaultAssemblyResolver();
            var directory = Path.GetDirectoryName(test_case.ModuleLocation);
            test_resolver.AddSearchDirectory(directory);
            return test_resolver;
        }

        ModuleDefinition RoundTrip(string location, ReaderParameters reader_parameters, string folder)
        {
            var rt_folder = Path.Combine(Path.GetTempPath(), folder);
            if (!Directory.Exists(rt_folder))
                Directory.CreateDirectory(rt_folder);
            var rt_module = Path.Combine(rt_folder, Path.GetFileName(location));

            using (var module = ModuleDefinition.ReadModule(location, reader_parameters))
            {
                var writer_parameters = new WriterParameters
                {
                    SymbolWriterProvider = GetSymbolWriterProvider(),
                };

                test_case.Test(module);

                module.Write(rt_module, writer_parameters);
            }

            if (test_case.Verify)
                CompilationService.Verify(rt_module);

            return ModuleDefinition.ReadModule(rt_module, reader_parameters);
        }

        public void RunTest()
        {
            var module = GetModule();
            if (module == null)
                return;

            test_module = module;
            test_case.Test(module);
        }

        public void Dispose()
        {
            if (test_module != null)
                test_module.Dispose();

            if (test_resolver != null)
                test_resolver.Dispose();
        }
    }

    enum TestCaseType
    {
        ReadImmediate,
        ReadDeferred,
        WriteFromImmediate,
        WriteFromDeferred,
    }
}
