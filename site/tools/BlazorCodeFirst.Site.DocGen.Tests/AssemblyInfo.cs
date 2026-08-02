// The DocGen conversion tool is used sequentially (DocGenRunner converts files in a
// foreach). MarkdownConverter reuses a single Markdig pipeline whose ColorCode formatter
// holds mutable TextWriter state that is safe for sequential reuse but NOT for concurrent
// reuse. Run tests serially to mirror the tool's real (sequential) usage.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
