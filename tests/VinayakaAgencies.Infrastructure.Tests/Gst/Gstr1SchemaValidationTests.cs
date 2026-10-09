using System.Text.Json.Nodes;
using Json.Schema;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Gst;

/// <summary>
/// Checkpoint C5.1 — "a generated GSTR-1 JSON validates against the current GSTN schema for a test
/// dataset" — as far as it can currently be taken.
/// </summary>
/// <remarks>
/// <b>The authoritative GSTN schema is not in this repository.</b> It is published behind the
/// authenticated GSTN developer portal, so it has to be fetched by the owner; that is tracked as
/// <c>open-questions.md</c> #23, the same shape of blocked external dependency as the NIC sandbox
/// credentials (#10) and the real Tally export (#22). Until it lands, C5.1 is **partially met** and
/// this file is deliberately not named as proof of it.
/// <para>
/// What these tests do give is the half that does not need GSTN: the shape this build emits is
/// pinned in <c>Schemas/gstr1-as-built.schema.json</c> and validated with a real JSON Schema
/// evaluator, so a field renamed, retyped, dropped or added in <c>Gstr1JsonExporter</c> fails here
/// rather than at the portal. That lock has already earned itself — the C5.1 review it was written
/// for found the cess amount under <c>csft</c> instead of <c>csamt</c>, the HSN code under
/// <c>hsn</c> instead of <c>hsn_sc</c>, and a B2B invoice's rate figures written flat on the
/// <c>itms</c> entry instead of nested under <c>num</c> + <c>itm_det</c>. All three are fixed; cess
/// in particular would have filed as nil.
/// </para>
/// <para>
/// When the real schema arrives, drop it in beside the as-built one as
/// <c>Schemas/gstr1-gstn.schema.json</c> (the csproj globs the folder) and add the test that
/// evaluates an export against it. That test is what closes C5.1 — not this one.
/// </para>
/// </remarks>
public sealed class Gstr1SchemaValidationTests
{
    [Fact]
    public void AsBuiltSchema_IsLoadableAndConstrainsSomething()
    {
        // Without this, every other assertion in this file could be passing against a schema that
        // failed to load or asserts nothing, which is the usual way schema validation becomes
        // theatre.
        var empty = Gstr1Schema.Evaluate(Gstr1Schema.AsBuiltFileName, JsonNode.Parse("{}")!);

        empty.IsValid.ShouldBeFalse("an empty object must not satisfy the GSTR-1 shape");
    }

    [Fact]
    public void AsBuiltSchema_RejectsAnExportWhoseCessFieldRevertsToTheWrongName()
    {
        // The exact regression the C5.1 review found. Renaming csamt back to csft must fail.
        var document = MinimalExport();
        var totals = document["gtotals"]!.AsObject();
        var cess = totals["csamt"]!.GetValue<decimal>();

        totals.Remove("csamt");
        totals["csft"] = cess;

        Evaluate(document).IsValid.ShouldBeFalse(
            "cess under csft is not a field the portal recognises, so it must not validate");
    }

    [Fact]
    public void AsBuiltSchema_RejectsAB2BInvoiceWhoseRateFiguresAreNotNestedUnderItmDet()
    {
        // The other half of the same review: itms entries carrying rt/txval flat.
        var document = MinimalExport();
        var items = document["b2b"]![0]!["inv"]![0]!["itms"]!.AsArray();
        var detail = items[0]!["itm_det"]!.DeepClone();

        items.Clear();
        items.Add(detail);

        Evaluate(document).IsValid.ShouldBeFalse(
            "a flat rate object on itms is the shape that was wrong; it must not validate");
    }

    [Fact]
    public void AsBuiltSchema_RejectsAnUnknownField()
    {
        var document = MinimalExport();
        document["surprise"] = "value";

        Evaluate(document).IsValid.ShouldBeFalse(
            "the lock is additionalProperties:false — a new field must be a deliberate schema change");
    }

    [Fact]
    public void AsBuiltSchema_AcceptsTheShapeTheExporterWrites()
    {
        var result = Evaluate(MinimalExport());

        result.IsValid.ShouldBeTrue(Explain(result));
    }

    // ------------------------------------------------------------------------------- helpers

    private static EvaluationResults Evaluate(JsonNode document) =>
        Gstr1Schema.Evaluate(Gstr1Schema.AsBuiltFileName, document);

    private static string Explain(EvaluationResults results) => Gstr1Schema.Explain(results);

    /// <summary>
    /// One well-formed export, written out by hand rather than generated.
    /// </summary>
    /// <remarks>
    /// The end-to-end counterpart — a real export from real posted invoices — is asserted by
    /// <c>GstReturnWorkflowTests.ExportGstr1_ValidatesAgainstTheAsBuiltSchema</c>, which needs a
    /// database. These tests mutate one field at a time to prove the schema bites, and for that a
    /// literal document is clearer than arranging a company to produce each variation.
    /// </remarks>
    private static JsonObject MinimalExport() => JsonNode
        .Parse(
            """
            {
              "version": "v3.2.4",
              "gstin": "29ABCDE1234F1Z5",
              "fp": "202627",
              "gen_dt": "2026-08-31T00:00:00.0000000+00:00",
              "gtotals": { "txval": 1500.00, "iamt": 180.00, "camt": 45.00, "samt": 45.00, "csamt": 0.00 },
              "b2b": [
                {
                  "ctin": "27AAPFU0939F1ZV",
                  "inv": [
                    {
                      "inum": "INV/0001",
                      "idt": "20-08-2026",
                      "val": 1180.00,
                      "pos": "27",
                      "rchrg": "N",
                      "itms": [
                        {
                          "num": 1,
                          "itm_det": { "rt": 18.00, "txval": 1000.00, "iamt": 180.00, "camt": 0.00, "samt": 0.00, "csamt": 0.00 }
                        }
                      ]
                    }
                  ]
                }
              ],
              "b2c": [
                {
                  "pos": "29",
                  "lines": [
                    { "rt": 18.00, "cnt": 1, "val": 590.00, "txval": 500.00, "iamt": 0.00, "camt": 45.00, "samt": 45.00, "csamt": 0.00 }
                  ]
                }
              ],
              "hsn": {
                "data": [
                  {
                    "num": 1,
                    "hsn_sc": "84137010",
                    "desc": "Centrifugal pumps",
                    "uqc": "NOS",
                    "qty": 2.0,
                    "txval": 1000.00,
                    "iamt": 180.00,
                    "camt": 0.00,
                    "samt": 0.00,
                    "csamt": 0.00,
                    "sply_ty": "b2b"
                  }
                ]
              },
              "docs": {
                "inv": [
                  { "num": 1, "from": "INV/0001", "to": "INV/0002", "tot": 2, "cancel": 0, "net": 2 }
                ]
              }
            }
            """)!
        .AsObject();
}
