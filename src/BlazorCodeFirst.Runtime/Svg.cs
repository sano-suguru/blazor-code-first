namespace BlazorCodeFirst;

/// <summary>
/// Design-time syntax for the SVG2 element vocabulary (#319 / #534). Independent of <see cref="Html"/>:
/// it sits outside <c>DESIGN.md</c> §4.1 group 5's curated `Html` set rather than against it, since SVG
/// element names break that set's "capitalize only the first letter, tag stays all-lowercase" invariant
/// (<c>clipPath</c>, <c>linearGradient</c>, <c>feGaussianBlur</c>, ...). The naming rule here is the same
/// "capitalize only the tag name's first letter", but the rest of a tag's casing is preserved rather than
/// forced to lowercase, and it is total over all 69 elements of the SVG2 element index (none are
/// hyphenated).
/// </summary>
public static class Svg
{
    /// <summary>Design-time syntax for the SVG root <c>svg</c> element; supply children with
    /// <c>[…]</c>. Named <c>Root</c> rather than following the mechanical rule (which would collide with
    /// the enclosing class, CS0542) since it is the one irregular spot in an otherwise total naming
    /// rule.</summary>
    public static ElementView Root => default;

    /// <summary>Design-time syntax for an SVG <c>a</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementView A => default;

    /// <summary>Design-time syntax for an SVG <c>animate</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Animate => default;

    /// <summary>Design-time syntax for an SVG <c>animateMotion</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView AnimateMotion => default;

    /// <summary>Design-time syntax for an SVG <c>animateTransform</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView AnimateTransform => default;

    /// <summary>Design-time syntax for an SVG <c>audio</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Audio => default;

    /// <summary>Design-time syntax for an SVG <c>canvas</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Canvas => default;

    /// <summary>Design-time syntax for an SVG <c>circle</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Circle => default;

    /// <summary>Design-time syntax for an SVG <c>clipPath</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView ClipPath => default;

    /// <summary>Design-time syntax for an SVG <c>defs</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Defs => default;

    /// <summary>Design-time syntax for an SVG <c>desc</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Desc => default;

    /// <summary>Design-time syntax for an SVG <c>discard</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Discard => default;

    /// <summary>Design-time syntax for an SVG <c>ellipse</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Ellipse => default;

    /// <summary>Design-time syntax for an SVG <c>feBlend</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeBlend => default;

    /// <summary>Design-time syntax for an SVG <c>feColorMatrix</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeColorMatrix => default;

    /// <summary>Design-time syntax for an SVG <c>feComponentTransfer</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeComponentTransfer => default;

    /// <summary>Design-time syntax for an SVG <c>feComposite</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeComposite => default;

    /// <summary>Design-time syntax for an SVG <c>feConvolveMatrix</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeConvolveMatrix => default;

    /// <summary>Design-time syntax for an SVG <c>feDiffuseLighting</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeDiffuseLighting => default;

    /// <summary>Design-time syntax for an SVG <c>feDisplacementMap</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeDisplacementMap => default;

    /// <summary>Design-time syntax for an SVG <c>feDistantLight</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeDistantLight => default;

    /// <summary>Design-time syntax for an SVG <c>feDropShadow</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeDropShadow => default;

    /// <summary>Design-time syntax for an SVG <c>feFlood</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeFlood => default;

    /// <summary>Design-time syntax for an SVG <c>feFuncA</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeFuncA => default;

    /// <summary>Design-time syntax for an SVG <c>feFuncB</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeFuncB => default;

    /// <summary>Design-time syntax for an SVG <c>feFuncG</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeFuncG => default;

    /// <summary>Design-time syntax for an SVG <c>feFuncR</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeFuncR => default;

    /// <summary>Design-time syntax for an SVG <c>feGaussianBlur</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeGaussianBlur => default;

    /// <summary>Design-time syntax for an SVG <c>feImage</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeImage => default;

    /// <summary>Design-time syntax for an SVG <c>feMerge</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeMerge => default;

    /// <summary>Design-time syntax for an SVG <c>feMergeNode</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeMergeNode => default;

    /// <summary>Design-time syntax for an SVG <c>feMorphology</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeMorphology => default;

    /// <summary>Design-time syntax for an SVG <c>feOffset</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeOffset => default;

    /// <summary>Design-time syntax for an SVG <c>fePointLight</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FePointLight => default;

    /// <summary>Design-time syntax for an SVG <c>feSpecularLighting</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeSpecularLighting => default;

    /// <summary>Design-time syntax for an SVG <c>feSpotLight</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeSpotLight => default;

    /// <summary>Design-time syntax for an SVG <c>feTile</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeTile => default;

    /// <summary>Design-time syntax for an SVG <c>feTurbulence</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView FeTurbulence => default;

    /// <summary>Design-time syntax for an SVG <c>filter</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Filter => default;

    /// <summary>Design-time syntax for an SVG <c>foreignObject</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView ForeignObject => default;

    /// <summary>Design-time syntax for an SVG <c>g</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementView G => default;

    /// <summary>Design-time syntax for an SVG <c>iframe</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Iframe => default;

    /// <summary>Design-time syntax for an SVG <c>image</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Image => default;

    /// <summary>Design-time syntax for an SVG <c>line</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Line => default;

    /// <summary>Design-time syntax for an SVG <c>linearGradient</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView LinearGradient => default;

    /// <summary>Design-time syntax for an SVG <c>marker</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Marker => default;

    /// <summary>Design-time syntax for an SVG <c>mask</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Mask => default;

    /// <summary>Design-time syntax for an SVG <c>metadata</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Metadata => default;

    /// <summary>Design-time syntax for an SVG <c>mpath</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Mpath => default;

    /// <summary>Design-time syntax for an SVG <c>path</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Path => default;

    /// <summary>Design-time syntax for an SVG <c>pattern</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Pattern => default;

    /// <summary>Design-time syntax for an SVG <c>polygon</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Polygon => default;

    /// <summary>Design-time syntax for an SVG <c>polyline</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Polyline => default;

    /// <summary>Design-time syntax for an SVG <c>radialGradient</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView RadialGradient => default;

    /// <summary>Design-time syntax for an SVG <c>rect</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Rect => default;

    /// <summary>Design-time syntax for an SVG <c>script</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Script => default;

    /// <summary>Design-time syntax for an SVG <c>set</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Set => default;

    /// <summary>Design-time syntax for an SVG <c>stop</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Stop => default;

    /// <summary>Design-time syntax for an SVG <c>style</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Style => default;

    /// <summary>Design-time syntax for an SVG <c>switch</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Switch => default;

    /// <summary>Design-time syntax for an SVG <c>symbol</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Symbol => default;

    /// <summary>Design-time syntax for an SVG <c>text</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Text => default;

    /// <summary>Design-time syntax for an SVG <c>textPath</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView TextPath => default;

    /// <summary>Design-time syntax for an SVG <c>title</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Title => default;

    /// <summary>Design-time syntax for an SVG <c>tspan</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Tspan => default;

    /// <summary>Design-time syntax for an SVG <c>unknown</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Unknown => default;

    /// <summary>Design-time syntax for an SVG <c>use</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Use => default;

    /// <summary>Design-time syntax for an SVG <c>video</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView Video => default;

    /// <summary>Design-time syntax for an SVG <c>view</c> element; supply children with
    /// <c>[…]</c>.</summary>
    public static ElementView View => default;
}
