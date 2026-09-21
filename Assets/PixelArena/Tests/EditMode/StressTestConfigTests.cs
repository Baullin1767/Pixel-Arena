using System;
using NUnit.Framework;

public sealed class StressTestConfigTests
{
    static Type ConfigType => Type.GetType("PixelArena.StressTestConfig, Assembly-CSharp", true);
    static object Parse(params string[] args) => ConfigType.GetMethod("Parse").Invoke(null, new object[] { args });
    static object Value(object target, string name) => ConfigType.GetField(name).GetValue(target);
    static object Property(object target, string name) => ConfigType.GetProperty(name).GetValue(target);

    [Test]
    public void HundredBotsTwentyRoomsProducesFivePlayerRooms()
    {
        var config = Parse("game", "--stress-server", "--bots", "100", "--rooms", "20");
        Assert.That(Value(config, "Mode").ToString(), Is.EqualTo("Server"));
        Assert.That(Property(config, "RoomCapacity"), Is.EqualTo(5));
    }

    [Test]
    public void InvalidAndOutOfRangeArgumentsAreSafe()
    {
        var config = Parse("game", "--stress-runner", "--bots", "0", "--rooms", "999", "--port", "70000");
        Assert.That(Value(config, "Bots"), Is.EqualTo(1));
        Assert.That(Value(config, "Rooms"), Is.EqualTo(1));
        Assert.That(Value(config, "Port"), Is.EqualTo((int)ushort.MaxValue));
        Assert.That(Property(config, "RoomCapacity"), Is.EqualTo(1));
    }
}
