using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// The Data Protection key ring in a list.
/// </summary>
/// <remarks>
/// The gateway keeps its ring in Redis (ADR 0024), and the framework eagerly initializes the ring at startup —
/// so without this every test host would spend five seconds failing to reach a Redis that is not there. The rest
/// of the Data Protection stack is the real one, which is what antiforgery needs to behave as it does in
/// production.
/// </remarks>
internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];

    public IReadOnlyCollection<XElement> GetAllElements() => [.. elements];

    public void StoreElement(XElement element, string friendlyName) => elements.Add(element);
}
