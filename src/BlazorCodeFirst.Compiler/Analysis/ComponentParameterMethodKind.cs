namespace BlazorCodeFirst.Compiler.Analysis;

internal enum ComponentParameterMethodKind
{
    None,
    ScalarParam,
    FragmentParam,
    GenericTemplateIgnored,
    GenericTemplateContextual,
}
