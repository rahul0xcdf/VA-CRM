using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Application.Interop;

/// <summary>A saved mapping, as the picker lists it.</summary>
public sealed record ExcelColumnMappingSummary(
    Guid Id,
    string Name,
    ExcelImportTarget Target,
    string? SheetName,
    int HeaderRowNumber,
    int ColumnCount,
    bool IsActive);

/// <summary>One column of a mapping: the owner's header, and the field it feeds.</summary>
public sealed record ExcelColumnMappingLine(string SourceHeader, string TargetField);

/// <summary>A mapping and its columns, as the editor shows it.</summary>
public sealed record ExcelColumnMappingDetail(
    Guid Id,
    string Name,
    ExcelImportTarget Target,
    string? SheetName,
    int HeaderRowNumber,
    IReadOnlyList<ExcelColumnMappingLine> Columns,
    bool IsActive);

/// <summary>Creates or replaces a mapping. A null <paramref name="Id"/> creates.</summary>
public sealed record SaveExcelColumnMappingRequest(
    Guid? Id,
    string Name,
    ExcelImportTarget Target,
    IReadOnlyList<ExcelColumnMappingLine> Columns,
    string? SheetName = null,
    int HeaderRowNumber = 1);

/// <summary>
/// A column of a real sheet, with the field this application thinks it feeds.
/// </summary>
/// <remarks>
/// The mapping screen opens on this rather than on an empty grid. Most sheets name their columns
/// something close to what the field is called, and making the owner re-state that for every column
/// of every sheet is the kind of work software should do first and ask about second.
/// <see cref="TargetField"/> is null where nothing matched, which is what the owner then fills in.
/// </remarks>
public sealed record SuggestedColumn(string SourceHeader, string? TargetField, bool IsRequiredField);
