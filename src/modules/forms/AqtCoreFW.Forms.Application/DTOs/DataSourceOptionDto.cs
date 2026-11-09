namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>A single option returned by a data source resolver (EnumRef/InternalRef/ExternalRef).</summary>
public sealed record DataSourceOptionDto(string Value, string Label, string? LabelVi = null);
