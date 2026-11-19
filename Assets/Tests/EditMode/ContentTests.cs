using System.IO;
using Boll.Core.Content;
using NUnit.Framework;
using UnityEngine;

namespace Boll.Tests
{
    public class ContentTests
    {
        // Пул-стол 2.54 x 1.27 м; центр в (0,0).
        const string Good = @"{
 ""id"":""t1"",""name"":""Тест"",""kind"":""eight"",
 ""outline"":[[-1.27,-0.635],[1.27,-0.635],[1.27,0.635],[-1.27,0.635]],
 ""pockets"":[{""pos"":[-1.27,-0.635]},{""pos"":[0,-0.66]},{""pos"":[1.27,-0.635]},{""pos"":[-1.27,0.635]},{""pos"":[0,0.66]},{""pos"":[1.27,0.635]}],
 ""obstacles"":[{""type"":""bumper"",""c"":[0,0],""r"":0.05,""k"":1.2},
   {""type"":""mover"",""a"":[0.5,-0.2],""b"":[0.5,0.2],""speed"":0.3,""mode"":""pingpong"",""path"":[[0,0],[0,0.3]]}],
 ""layout"":{""rackPoint"":[0.6,0],""rackAxis"":[1,0],""home"":[[-1.2,-0.58],[-0.6,-0.58],[-0.6,0.58],[-1.2,0.58]],
   ""homeLineA"":[-0.6,-0.6],""homeLineB"":[-0.6,0.6],""returnPoints"":[[-0.9,0],[0.9,0.3]]}
}";

        [Test]
        public void ValidMapLoads()
        {
            var m = MapLoader.Load(Good);
            Assert.AreEqual(GameKind.Eight, m.Kind);
            Assert.AreEqual(0.05715, m.BallDiameter, 1e-9);
            Assert.AreEqual(6, m.Pockets.Length);
            Assert.AreEqual(0.115, m.Pockets[0].Throat, 1e-9);
            Assert.AreEqual(2, m.Obstacles.Length);
            Assert.Greater(Geo.Area2(m.Outline), 0);
        }

        [TestCase("\"k\":1.2", "\"k\":1.9")]
        [TestCase("\"kind\":\"eight\"", "\"kind\":\"golf\"")]
        [TestCase("\"rackPoint\":[0.6,0]", "\"rackPoint\":[0.0,0.0]")]   // перекрыт бампером
        [TestCase("\"rackAxis\":[1,0]", "\"rackAxis\":[2,0]")]
        [TestCase("[1.27,0.635],[-1.27,0.635]]", "[-1.27,-0.635],[-1.27,0.635]]")] // самопересечение/вырождение
        [TestCase("\"path\":[[0,0],[0,0.3]]", "\"path\":[[0.1,0],[0,0.3]]")]
        public void BadMapRejected(string from, string to)
        {
            Assert.IsTrue(Good.Contains(from));
            Assert.Throws<ContentException>(() => MapLoader.Load(Good.Replace(from, to)));
        }

        [Test]
        public void BrokenJsonRejected() => Assert.Throws<ContentException>(() => MapLoader.Load("{"));

        [Test]
        public void ShippedCatalogsValid()
        {
            string d = Path.Combine(Application.dataPath, "Resources", "catalog");
            var c = CatalogLoader.LoadAll(File.ReadAllText(Path.Combine(d, "physics.json")),
                File.ReadAllText(Path.Combine(d, "bots.json")), File.ReadAllText(Path.Combine(d, "skins.json")));
            Assert.AreEqual("medium", c.DefaultModel);
            Assert.AreEqual(1.075, c.Model("medium").PocketCapture, 1e-9);
            Assert.AreEqual(8, c.Bots.Length);
            Assert.AreEqual(8, c.Bots[7].Lookahead);
            Assert.AreEqual(3, c.Cloths.Length);
            Assert.AreEqual(3, c.Cues.Length);
        }

        [Test]
        public void MapCatalogNextAvoidsCurrent()
        {
            var cat = new MapCatalog(new[] { (GameKind.Eight, Good), (GameKind.Eight, Good.Replace("\"t1\"", "\"t2\"")) });
            Assert.AreEqual("t2", cat.Next(GameKind.Eight, "t1", n => 0).Id);
            Assert.Throws<ContentException>(() => new MapCatalog(new[] { (GameKind.Eight, Good), (GameKind.Eight, Good) }));
        }
    }
}
