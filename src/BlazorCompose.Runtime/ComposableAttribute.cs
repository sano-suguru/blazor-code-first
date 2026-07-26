namespace BlazorCompose;

/// <summary>
/// Marks a method as a composable UI fragment that the BlazorCompose source generator may analyze and
/// statically expand into a component's generated <c>RenderView</c>.
/// </summary>
/// <remarks>
/// A <c>[Composable]</c> method returns a design-time <see cref="View"/> expression; like
/// <see cref="ComposeComponentBase.Body"/> or <see cref="ComposeLayoutBase.Chrome"/>, it is analyzed
/// statically and is not intended to run at runtime.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ComposableAttribute : Attribute;
