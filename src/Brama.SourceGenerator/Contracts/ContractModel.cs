using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Brama.SourceGenerator.Contracts;

/// <summary>One field of a request or response shape.</summary>
internal sealed record ContractField(string Name, string Type);

/// <summary>A request or response type, reduced to the part a client can observe.</summary>
internal sealed record ContractShape(string Type, IReadOnlyList<ContractField> Fields);

/// <summary>One endpoint's externally visible surface.</summary>
internal sealed record ContractEndpoint(
    string Method,
    string Route,
    ContractShape Response,
    IReadOnlyList<ContractShape> Requests);

/// <summary>
/// The frozen surface of one group at one version — the unit that is supposed to stay stable, and
/// therefore the unit that gets a committed snapshot.
/// </summary>
internal sealed record ContractGroup(
    string Name,
    int Version,
    string? AcceptBreakingChangeReason,
    IReadOnlyList<ContractEndpoint> Endpoints,
    IReadOnlyList<KeyValuePair<string, int>> ErrorStatusCodes);

/// <summary>
/// Renders a contract as a sorted list of self-contained facts, one per line.
/// </summary>
/// <remarks>
/// Deliberately not JSON, which an earlier draft of the design specified. Every line here carries
/// its own full path, which makes the compatibility rule exact and trivial to state: <em>a change is
/// breaking if any committed line is missing from the new build</em>. Nested JSON cannot support
/// that rule — a field is one fragment of a line shared with its siblings, so adding a field and
/// removing one are indistinguishable at line level, and telling them apart needs a bespoke
/// structural differ. It also makes the pull-request diff say precisely what broke, in words, which
/// is the whole point of committing the snapshot at all.
/// </remarks>
internal static class ContractFacts
{
    public static string Write(ContractGroup group)
    {
        var facts = new List<string>();

        foreach (KeyValuePair<string, int> status in group.ErrorStatusCodes)
        {
            facts.Add($"status  ErrorKind.{status.Key} = {status.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (ContractEndpoint endpoint in group.Endpoints)
        {
            string at = $"{endpoint.Method} {endpoint.Route}";

            facts.Add($"endpoint  {at}");
            facts.Add($"response  {at}  {endpoint.Response.Type}");

            foreach (ContractField field in endpoint.Response.Fields)
            {
                facts.Add($"response.field  {at}  {field.Name} : {field.Type}");
            }

            foreach (ContractShape request in endpoint.Requests)
            {
                facts.Add($"request  {at}  {request.Type}");

                foreach (ContractField field in request.Fields)
                {
                    facts.Add($"request.field  {at}  {request.Type}.{field.Name} : {field.Type}");
                }
            }
        }

        facts.Sort(StringComparer.Ordinal);

        return string.Join("\n", facts.Distinct(StringComparer.Ordinal));
    }
}
