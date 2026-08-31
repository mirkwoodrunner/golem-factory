using System.IO;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.Save;

namespace GolemFactory.Tests.EditMode
{
    public class SaveFileIOTests
    {
        private string _tempPath;

        [TearDown]
        public void TearDown()
        {
            if (_tempPath != null && File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }

        [Test]
        public void WriteThenRead_RoundTripsData()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "golem-factory-test-save.json");
            var data = new SaveData();
            var buffer = new BufferEntry { bufferId = "ScrapBuffer" };
            buffer.itemTypes.Add(ItemType.Scrap);
            buffer.quantities.Add(55);
            data.buffers.Add(buffer);

            SaveFileIO.WriteToFile(data, _tempPath);
            SaveData loaded = SaveFileIO.ReadFromFile(_tempPath);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("ScrapBuffer", loaded.buffers[0].bufferId);
            Assert.AreEqual(ItemType.Scrap, loaded.buffers[0].itemTypes[0]);
            Assert.AreEqual(55, loaded.buffers[0].quantities[0]);
        }

        [Test]
        public void ReadFromFile_MissingFile_ReturnsNull()
        {
            SaveData loaded = SaveFileIO.ReadFromFile(Path.Combine(Path.GetTempPath(), "no-such-golem-factory-save.json"));

            Assert.IsNull(loaded);
        }
    }
}
