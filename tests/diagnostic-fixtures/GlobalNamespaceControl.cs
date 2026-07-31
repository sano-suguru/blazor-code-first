// Deliberately declared in the global namespace, which violates CA1050. It is the control for the
// whole fixture: an unrelated analyzer rule that must be reported here, proving the analyzer driver
// ran, and that must be absent from the GeneratorDelivery fixtures, where a declaration-level error
// stops that driver from running at all. Do not move this into a namespace.
public sealed class GlobalNamespaceControl;
