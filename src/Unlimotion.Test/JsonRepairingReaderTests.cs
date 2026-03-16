using System.Threading.Tasks;

namespace Unlimotion.Test;

public class JsonRepairingReaderTests
{
    ///<summary>
    ///Тест для тестирования JsonRepairingReader.FixMissingCommas
    ///</summary>
    [Test]
    public async Task RepairJson_Success()
    {
        var broken = @"{
    ""Id"": ""1""
    ""Title"": ""Task 1""
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        await Assert.That(repair).IsEqualTo(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1""
}");
    }

    [Test]
    public async Task RepairInnerJson_Success()
    {
        var broken = @"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2""
        ""Item 3""
    ]
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        await Assert.That(repair).IsEqualTo(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}");
    }

    [Test]
    public async Task RepairInner2Json_Success()
    {
        var broken = @"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1""
        ""Item 2"",
        ""Item 3""
    ]
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        await Assert.That(repair).IsEqualTo(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}");
    }

    [Test]
    public async Task RepairInner3Json_Success()
    {
        var broken = @"{
    ""Id"": ""1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
    ""Title"": ""Task 1""
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        await Assert.That(repair).IsEqualTo(@"{
    ""Id"": ""1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ],
    ""Title"": ""Task 1""
}");
    }

    [Test]
    public async Task RepairInner4Json_Success()
    {
        var broken = @"{
    ""Id"": ""1"",
    ""Title"": ""Task 1""
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        await Assert.That(repair).IsEqualTo(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}");
    }
}