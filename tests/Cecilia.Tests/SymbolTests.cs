
using Cecilia.Cil;
using NUnit.Framework;

namespace Cecilia.Tests
{
    [TestFixture]
    public class SymbolTests : BaseTestFixture
    {
        [Test]
        public void DefaultPortablePdb()
        {
            TestModule("PdbTarget.exe", module =>
            {
                Assert.IsTrue(module.HasSymbols);
                Assert.AreEqual(typeof(PortablePdbReader), module.SymbolReader.GetType());
            }, symbolReaderProvider: typeof(DefaultSymbolReaderProvider), symbolWriterProvider: typeof(DefaultSymbolWriterProvider));
        }

        [Test]
        public void DefaultEmbeddedPortablePdb()
        {
            TestModule("EmbeddedPdbTarget.exe", module =>
            {
                Assert.IsTrue(module.HasSymbols);
                Assert.AreEqual(typeof(PortablePdbReader), module.SymbolReader.GetType());
            }, symbolReaderProvider: typeof(DefaultSymbolReaderProvider), symbolWriterProvider: typeof(DefaultSymbolWriterProvider), verify: !Platform.OnMono);
        }

        [Test]
        public void PortablePdbMismatch()
        {
            Assert.Throws<SymbolsNotMatchingException>(() => GetResourceModule("pdb-mismatch.dll", new ReaderParameters { SymbolReaderProvider = new PortablePdbReaderProvider() }));
        }

        [Test]
        public void PortablePdbIgnoreMismatch()
        {
            using (var module = GetResourceModule("pdb-mismatch.dll", new ReaderParameters { SymbolReaderProvider = new PortablePdbReaderProvider(), ThrowIfSymbolsAreNotMatching = false }))
            {
                Assert.IsNull(module.SymbolReader);
                Assert.IsFalse(module.HasSymbols);
            }
        }

        [Test]
        public void DefaultPortablePdbStream()
        {
            using (var symbolStream = GetResourceStream("PdbTarget.pdb"))
            {
                var parameters = new ReaderParameters
                {
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    SymbolStream = symbolStream,
                };

                using (var module = GetResourceModule("PdbTarget.exe", parameters))
                {
                    Assert.IsNotNull(module.SymbolReader);
                    Assert.IsTrue(module.HasSymbols);
                    Assert.AreEqual(typeof(PortablePdbReader), module.SymbolReader.GetType());
                }
            }
        }

        [Test]
        public void MultipleCodeViewEntries()
        {
            using (var module = GetResourceModule("System.Private.Xml.dll", new ReaderParameters { ReadSymbols = true }))
            {
                Assert.IsTrue(module.HasSymbols);
                Assert.IsNotNull(module.SymbolReader);
            }
        }
    }
}
