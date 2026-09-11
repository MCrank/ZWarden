using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Ids;

namespace ZWarden.Infrastructure.Tests.Ids;

/// <summary>The EF value converter maps a typed id to its native Guid and back (S4, ADR 0004).</summary>
public class TypedIdValueConverterTests
{
    [Test]
    public async Task Converts_a_typed_id_to_its_native_guid_and_back()
    {
        ValueConverter<AgentId, Guid> converter = TypedIdValueConverters.For<AgentId>();
        AgentId id = AgentId.New();

        object? provider = converter.ConvertToProvider(id);
        await Assert.That(provider).IsEqualTo(id.Value);

        object? model = converter.ConvertFromProvider(id.Value);
        await Assert.That(model).IsEqualTo(id);
    }

    [Test]
    public async Task Round_trip_is_identity()
    {
        ValueConverter<ServerId, Guid> converter = TypedIdValueConverters.For<ServerId>();
        ServerId original = ServerId.New();

        var back = (ServerId)converter.ConvertFromProvider(converter.ConvertToProvider(original)!)!;

        await Assert.That(back).IsEqualTo(original);
    }
}
