namespace AqtCoreFW.Forms.Domain.Exceptions;

/// <summary>Thrown when FormTemplate SchemaJson fails structural validation.</summary>
public sealed class InvalidFormSchemaException(string reason)
    : Exception($"Invalid form schema: {reason}");
