using System.IO;
using GolemFactory.Save;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>Unity's EditMode SaveFileIOTests, against the engine-free SaveFileIO (G9).</summary>
    public class SaveFileIOTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "golem-factory-test-" + System.Guid.NewGuid() + ".json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [Test]
        public void WriteThenRead_RoundTripsData()
        {
            var data = new SaveData();
            var buffer = new BufferEntry { bufferId = "FactoryStockpile" };
            buffer.itemTypes.Add("Scrap");
            buffer.quantities.Add(42);
            data.buffers.Add(buffer);
            data.golems.Add(new GolemEntry { golemId = "PlayerGolem-001", cellX = 3, cellY = -2, facing = 1, wasRuntimeSpawned = true });

            SaveFileIO.WriteToFile(data, _path);
            SaveData read = SaveFileIO.ReadFromFile(_path);

            Assert.AreEqual("FactoryStockpile", read.buffers[0].bufferId);
            Assert.AreEqual(42, read.buffers[0].quantities[0]);
            Assert.AreEqual("PlayerGolem-001", read.golems[0].golemId);
            Assert.AreEqual(-2, read.golems[0].cellY);
            Assert.IsTrue(read.golems[0].wasRuntimeSpawned);
        }

        [Test]
        public void ReadFromFile_MissingFile_ReturnsNull()
        {
            Assert.IsNull(SaveFileIO.ReadFromFile(_path));
        }
    }
}
