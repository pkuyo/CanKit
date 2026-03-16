namespace CanKit.Adapter.ZLG.Definitions;

public record struct ZlgServerInfo(string AuthServer, int AuthPort, string Mqtt, int MqttPort);
