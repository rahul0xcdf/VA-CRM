namespace VinayakaAgencies.Domain.Trade;

/// <summary>
/// One replacement line handed to <see cref="TradeDocument.ResetLines"/>: the shape the editing
/// service carries between the old line rows it removes and the new ones it adds.
/// </summary>
/// <remarks>
/// The pricing-evidence trio travels with the rate: conversion never reprices, so a carried line
/// keeps both what pricing applied (<see cref="AppliedRateMinor"/>, <see cref="PriceSource"/>) and
/// whether an operator moved the final rate off it.
/// </remarks>
public sealed record TradeLineInput(
    Guid ItemId,
    Guid? GodownId,
    decimal Quantity,
    decimal Rate,
    decimal DiscountPercent,
    bool IsSerialTracked,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null,
    long? AppliedRateMinor = null,
    bool IsPriceOverridden = false,
    SalePriceSource PriceSource = SalePriceSource.Manual);
