using Xunit;

namespace Unlimotion.Test;

public class JsonRepairingReaderTests
{
    ///<summary>
    ///Тест для тестирования JsonRepairingReader.FixMissingCommas
    ///</summary>
    [Fact]
    public void RepairJson_Success()
    {
        var broken = @"{
    ""Id"": ""1""
    ""Title"": ""Task 1""
}";
        var repair = JsonRepairingReader.FixMissingCommas(broken);
        Assert.Equal(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1""
}", repair);
    }

    [Fact]
    public void RepairInnerJson_Success()
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
        Assert.Equal(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}", repair);
    }

    [Fact]
    public void RepairInner2Json_Success()
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
        Assert.Equal(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}", repair);
    }

    [Fact]
    public void RepairInner3Json_Success()
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
        Assert.Equal(@"{
    ""Id"": ""1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ],
    ""Title"": ""Task 1""
}", repair);
    }

    [Fact]
    public void RepairInner4Json_Success()
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
        Assert.Equal(@"{
    ""Id"": ""1"",
    ""Title"": ""Task 1"",
    ""Contains"": [
        ""Item 1"",
        ""Item 2"",
        ""Item 3""
    ]
}", repair);
    }
}