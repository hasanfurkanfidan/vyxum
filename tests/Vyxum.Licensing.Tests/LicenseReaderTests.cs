using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Vyxum.Licensing.Tests;

public class LicenseReaderTests
{
    private static JsonObject Envelope(string text) => JsonNode.Parse(text)!.AsObject();

    private static byte[] PayloadBytes(string text) => Convert.FromBase64String(Envelope(text)["payload"]!.GetValue<string>());

    [Fact]
    public void Signed_license_round_trips()
    {
        using var key = new TestKey("k1");
        var payload = TestLicenses.Sample() with { MaintenanceUntil = new DateOnly(2027, 10, 31), ResellerId = "R-7" };

        var text = key.Signer.Sign(payload);
        var result = key.Reader().Read(text);

        result.IsValid.ShouldBeTrue(result.Detail);
        result.Error.ShouldBeNull();
        result.ErrorCode.ShouldBeNull();
        result.License!.KeyId.ShouldBe("k1");
        result.License.RawText.ShouldBe(text);
        var read = result.License.Payload;
        read.LicenseId.ShouldBe(payload.LicenseId);
        read.Type.ShouldBe(LicenseType.Subscription);
        read.Licensee.ShouldBe(payload.Licensee);
        read.Modules.ShouldBe(["labels"]);
        read.Stations.ShouldBe(2);
        read.IssuedAt.ShouldBe(payload.IssuedAt);
        read.ValidFrom.ShouldBe(payload.ValidFrom);
        read.ValidUntil.ShouldBe(payload.ValidUntil);
        read.GraceDays.ShouldBe(30);
        read.MaintenanceUntil.ShouldBe(new DateOnly(2027, 10, 31));
        read.ResellerId.ShouldBe("R-7");
    }

    [Fact]
    public void File_uses_the_documented_envelope_and_camel_case_payload()
    {
        using var key = new TestKey("k1");
        var text = key.Signer.Sign(TestLicenses.Sample());

        var envelope = Envelope(text);
        envelope["format"]!.GetValue<string>().ShouldBe("vxlic/1");
        envelope["keyId"]!.GetValue<string>().ShouldBe("k1");
        Convert.FromBase64String(envelope["signature"]!.GetValue<string>()).Length.ShouldBe(64); // IEEE P1363, P-256
        var payload = JsonNode.Parse(PayloadBytes(text))!.AsObject();
        payload["type"]!.GetValue<string>().ShouldBe("subscription");
        payload["validFrom"]!.GetValue<string>().ShouldBe("2026-11-01");
        payload["licensee"]!["name"]!.GetValue<string>().ShouldBe("Örnek Gıda A.Ş.");
    }

    [Fact]
    public void Utf8_bytes_with_byte_order_mark_are_read()
    {
        using var key = new TestKey("k1");
        var bytes = Encoding.UTF8.GetPreamble().Concat(key.Signer.SignToUtf8(TestLicenses.Sample())).ToArray();

        key.Reader().Read(bytes).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_utf8_bytes_are_invalid_format()
    {
        using var key = new TestKey("k1");

        key.Reader().Read([0x7B, 0xFF, 0xFE, 0x7D]).ErrorCode.ShouldBe("invalid_format");
    }

    [Fact]
    public void One_changed_payload_byte_is_an_invalid_signature()
    {
        using var key = new TestKey("k1");
        var text = key.Signer.Sign(TestLicenses.Sample());
        var bytes = PayloadBytes(text);
        // Turn "stations":2 into "stations":9: still valid JSON, only the signature can catch it.
        var json = Encoding.UTF8.GetString(bytes);
        var index = json.IndexOf("\"stations\":2", StringComparison.Ordinal) + "\"stations\":".Length;
        index.ShouldBeGreaterThan(10);
        bytes[index] = (byte)'9';
        var envelope = Envelope(text);
        envelope["payload"] = Convert.ToBase64String(bytes);

        var result = key.Reader().Read(envelope.ToJsonString());

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe(LicenseReadError.InvalidSignature);
        result.ErrorCode.ShouldBe("invalid_signature");
    }

    [Fact]
    public void Signature_taken_from_another_license_is_invalid()
    {
        using var key = new TestKey("k1");
        var first = Envelope(key.Signer.Sign(TestLicenses.Sample()));
        var second = Envelope(key.Signer.Sign(TestLicenses.Sample() with { Stations = 50 }));
        first["signature"] = second["signature"]!.GetValue<string>();

        key.Reader().Read(first.ToJsonString()).ErrorCode.ShouldBe("invalid_signature");
    }

    [Fact]
    public void Unknown_key_id_is_rejected()
    {
        using var key = new TestKey("k1");
        using var other = new TestKey("k2");

        key.Reader().Read(other.Signer.Sign(TestLicenses.Sample())).ErrorCode.ShouldBe("unknown_key");
    }

    [Fact]
    public void Different_key_under_the_same_key_id_is_an_invalid_signature()
    {
        using var trusted = new TestKey("k1");
        using var forger = new TestKey("k1");

        trusted.Reader().Read(forger.Signer.Sign(TestLicenses.Sample())).ErrorCode.ShouldBe("invalid_signature");
    }

    [Fact]
    public void Empty_production_key_ring_trusts_nothing()
    {
        using var key = new TestKey("k1");

        new LicenseReader(LicenseKeyRing.Trusted).Read(key.Signer.Sign(TestLicenses.Sample())).ErrorCode.ShouldBe("unknown_key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"vxlic/1\"")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"payload\":\"e30=\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"signature\":\"AA==\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"payload\":\"e30=\",\"signature\":\"AA==\"}")]
    [InlineData("{\"keyId\":\"k1\",\"payload\":\"e30=\",\"signature\":\"AA==\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"payload\":\"%%%\",\"signature\":\"AA==\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"payload\":\"e30=\",\"signature\":\"not base64!\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":42,\"payload\":\"e30=\",\"signature\":\"AA==\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"keyId\":\"k2\",\"payload\":\"e30=\",\"signature\":\"AA==\"}")]
    [InlineData("{\"format\":\"vxlic/1\",\"keyId\":\"k1\",\"payload\":\"e30=\",\"signature\":\"AA==\"")]
    public void Malformed_envelope_is_invalid_format(string text)
    {
        using var key = new TestKey("k1");

        var result = key.Reader().Read(text);

        result.IsValid.ShouldBeFalse();
        result.ErrorCode.ShouldBe("invalid_format");
        result.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Null_text_is_invalid_format()
    {
        using var key = new TestKey("k1");

        key.Reader().Read((string?)null).ErrorCode.ShouldBe("invalid_format");
    }

    [Theory]
    [InlineData("vxlic/2")]
    [InlineData("VXLIC/1")]
    [InlineData("")]
    public void Wrong_format_string_is_unsupported(string format)
    {
        using var key = new TestKey("k1");
        var envelope = Envelope(key.Signer.Sign(TestLicenses.Sample()));
        envelope["format"] = format;

        key.Reader().Read(envelope.ToJsonString()).ErrorCode.ShouldBe("unsupported_format");
    }

    [Fact]
    public void Signature_of_wrong_length_is_invalid()
    {
        using var key = new TestKey("k1");
        var envelope = Envelope(key.Signer.Sign(TestLicenses.Sample()));
        envelope["signature"] = Convert.ToBase64String(new byte[10]);

        key.Reader().Read(envelope.ToJsonString()).ErrorCode.ShouldBe("invalid_signature");
    }

    [Fact]
    public void File_longer_than_4000_characters_is_too_large()
    {
        using var key = new TestKey("k1");
        var text = key.Signer.Sign(TestLicenses.Sample());
        var padded = text.Insert(1, new string(' ', 4001 - text.Length));
        padded.Length.ShouldBe(4001);

        key.Reader().Read(padded).ErrorCode.ShouldBe("too_large");
        key.Reader().Read(Encoding.UTF8.GetBytes(padded)).ErrorCode.ShouldBe("too_large");
        key.Reader().Read(new byte[20_000]).ErrorCode.ShouldBe("too_large");
    }

    [Fact]
    public void File_of_exactly_4000_characters_is_accepted()
    {
        using var key = new TestKey("k1");
        var text = key.Signer.Sign(TestLicenses.Sample());
        var padded = text.Insert(1, new string(' ', 4000 - text.Length));

        key.Reader().Read(padded).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Signer_refuses_a_file_over_the_limit()
    {
        using var key = new TestKey("k1");
        var modules = Enumerable.Range(0, 400).Select(i => $"module-{i}").ToArray();

        Should.Throw<ArgumentException>(() => key.Signer.Sign(TestLicenses.Sample() with { Modules = modules }));
    }

    [Fact]
    public void Unknown_fields_in_payload_and_envelope_are_ignored()
    {
        using var key = new TestKey("k1");
        var payload = JsonNode.Parse(PayloadBytes(key.Signer.Sign(TestLicenses.Sample())))!.AsObject();
        payload["futureField"] = new JsonObject { ["nested"] = 1 };
        payload["licensee"]!["country"] = "TR";
        var envelope = Envelope(key.Signer.SignPayloadBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())));
        envelope["comment"] = "issued by hand";

        var result = key.Reader().Read(envelope.ToJsonString());

        result.IsValid.ShouldBeTrue(result.Detail);
        result.License!.Payload.Licensee.Name.ShouldBe("Örnek Gıda A.Ş.");
    }

    [Fact]
    public void Missing_grace_days_defaults_to_30()
    {
        using var key = new TestKey("k1");
        var payload = JsonNode.Parse(PayloadBytes(key.Signer.Sign(TestLicenses.Sample())))!.AsObject();
        payload.Remove("graceDays").ShouldBeTrue();

        var result = key.Reader().Read(key.Signer.SignPayloadBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())));

        result.License!.Payload.GraceDays.ShouldBe(30);
    }

    [Theory]
    [InlineData("licenseId")]
    [InlineData("type")]
    [InlineData("licensee")]
    [InlineData("modules")]
    [InlineData("stations")]
    [InlineData("issuedAt")]
    [InlineData("validFrom")]
    [InlineData("validUntil")]
    public void Missing_required_payload_field_is_invalid_payload(string field)
    {
        using var key = new TestKey("k1");
        var payload = JsonNode.Parse(PayloadBytes(key.Signer.Sign(TestLicenses.Sample())))!.AsObject();
        payload.Remove(field).ShouldBeTrue();

        var result = key.Reader().Read(key.Signer.SignPayloadBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())));

        result.ErrorCode.ShouldBe("invalid_payload");
        result.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("""{"stations":0}""")]
    [InlineData("""{"stations":-1}""")]
    [InlineData("""{"stations":"2"}""")]
    [InlineData("""{"type":"lifetime"}""")]
    [InlineData("""{"type":2}""")]
    [InlineData("""{"licenseId":""}""")]
    [InlineData("""{"licensee":{"taxNumber":"1"}}""")]
    [InlineData("""{"licensee":{"name":"  "}}""")]
    [InlineData("""{"modules":["labels",""]}""")]
    [InlineData("""{"modules":"labels"}""")]
    [InlineData("""{"validFrom":"2026-13-01"}""")]
    [InlineData("""{"validUntil":"2026-10-31"}""")]
    [InlineData("""{"graceDays":-1}""")]
    [InlineData("""{"graceDays":3651}""")]
    [InlineData("""{"issuedAt":"yesterday"}""")]
    [InlineData("""{"type":"perpetual"}""")]
    [InlineData("""{"validUntil":null}""")]
    public void Out_of_range_payload_is_invalid_payload(string overrides)
    {
        using var key = new TestKey("k1");
        var payload = JsonNode.Parse(PayloadBytes(key.Signer.Sign(TestLicenses.Sample())))!.AsObject();
        foreach (var (name, value) in JsonNode.Parse(overrides)!.AsObject())
            payload[name] = value?.DeepClone();

        var result = key.Reader().Read(key.Signer.SignPayloadBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())));

        result.ErrorCode.ShouldBe("invalid_payload");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    public void Signed_payload_that_is_not_a_license_object_is_invalid_payload(string payload)
    {
        using var key = new TestKey("k1");

        key.Reader().Read(key.Signer.SignPayloadBytes(Encoding.UTF8.GetBytes(payload))).ErrorCode.ShouldBe("invalid_payload");
    }

    [Fact]
    public void Payload_is_not_parsed_before_the_signature_is_checked()
    {
        using var key = new TestKey("k1");
        var envelope = Envelope(key.Signer.Sign(TestLicenses.Sample()));
        envelope["payload"] = Convert.ToBase64String("not json"u8.ToArray());

        key.Reader().Read(envelope.ToJsonString()).ErrorCode.ShouldBe("invalid_signature");
    }

    [Fact]
    public void Perpetual_license_round_trips_without_valid_until()
    {
        using var key = new TestKey("k1");
        var payload = TestLicenses.Sample() with { Type = LicenseType.Perpetual, ValidUntil = null };

        var text = key.Signer.Sign(payload);

        JsonNode.Parse(PayloadBytes(text))!.AsObject().ContainsKey("validUntil").ShouldBeFalse();
        key.Reader().Read(text).License!.Payload.ValidUntil.ShouldBeNull();
    }

    [Fact]
    public void Signer_rejects_an_invalid_payload()
    {
        using var key = new TestKey("k1");

        Should.Throw<ArgumentException>(() => key.Signer.Sign(TestLicenses.Sample() with { Stations = 0 }));
        Should.Throw<ArgumentException>(() => key.Signer.Sign(TestLicenses.Sample() with { Type = LicenseType.Perpetual }));
    }

    [Fact]
    public void Key_ring_rejects_keys_that_are_not_p256_public_keys()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);

        Should.Throw<ArgumentException>(() => new LicenseKeyRing([KeyValuePair.Create("k", p384.ExportSubjectPublicKeyInfo())]));
        Should.Throw<ArgumentException>(() => new LicenseKeyRing([KeyValuePair.Create("k", new byte[] { 1, 2, 3 })]));
        Should.Throw<ArgumentException>(() => new LicenseSigner(p384, "k"));
    }
}
