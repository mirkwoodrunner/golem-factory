using System.IO;
using NUnit.Framework;
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
            var data = new SaveData { focusCurrent = 55f };
            data.buffers.Add(new BufferEntry { bufferId = "ScrapBuffer" });

            SaveFileIO.WriteToFile(data, _tempPath);
            SaveData loaded = SaveFileIO.ReadFromFile(_tempPath);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(55f, loaded.focusCurrent);
            Assert.AreEqual("ScrapBuffer", loaded.buffers[0].bufferId);
        }

        [Test]
        public void ReadFromFile_MissingFile_ReturnsNull()
        {
            SaveData loaded = SaveFileIO.ReadFromFile(Path.Combine(Path.GetTempPath(), "no-such-golem-factory-save.json"));

            Assert.IsNull(loaded);
        }
    }
}
