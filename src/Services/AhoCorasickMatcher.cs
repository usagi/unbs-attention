namespace UnbsAttention.Services;

internal sealed class AhoCorasickMatcher
{
 private readonly Node[] _nodes;

 private sealed class Node
 {
  public Dictionary<char, int> Next { get; } = new();

  public int Failure { get; set; }

  public List<int> Outputs { get; } = new();
 }

 public AhoCorasickMatcher(IReadOnlyList<string> patterns)
 {
  var nodes = new List<Node>
  {
   new(),
  };

  for (var i = 0; i < patterns.Count; i++)
  {
   AddPattern(nodes, patterns[i], i);
  }

  BuildFailureLinks(nodes);
  _nodes = nodes.ToArray();
 }

 public bool IsEmpty => _nodes.Length <= 1;

 public void CollectMatches(string? input, Action<int> onMatch)
 {
  if (string.IsNullOrWhiteSpace(input) || IsEmpty)
  {
   return;
  }

  var state = 0;

  foreach (var raw in input!)
  {
   var c = char.ToUpperInvariant(raw);

     var hasTransition = _nodes[state].Next.TryGetValue(c, out var next);
     while (state != 0 && !hasTransition)
   {
    state = _nodes[state].Failure;
        hasTransition = _nodes[state].Next.TryGetValue(c, out next);
   }

     state = hasTransition ? next : 0;

   var outputs = _nodes[state].Outputs;
   for (var i = 0; i < outputs.Count; i++)
   {
    onMatch(outputs[i]);
   }
  }
 }

 private static void AddPattern(List<Node> nodes, string pattern, int index)
 {
  if (string.IsNullOrWhiteSpace(pattern))
  {
   return;
  }

  var state = 0;
  foreach (var raw in pattern)
  {
   var c = char.ToUpperInvariant(raw);
   if (!nodes[state].Next.TryGetValue(c, out var next))
   {
    next = nodes.Count;
    nodes[state].Next[c] = next;
    nodes.Add(new Node());
   }

   state = next;
  }

  nodes[state].Outputs.Add(index);
 }

 private static void BuildFailureLinks(List<Node> nodes)
 {
  var queue = new Queue<int>();

  foreach (var next in nodes[0].Next.Values)
  {
   nodes[next].Failure = 0;
   queue.Enqueue(next);
  }

  while (queue.Count > 0)
  {
   var state = queue.Dequeue();
   foreach (var transition in nodes[state].Next)
   {
    var c = transition.Key;
    var next = transition.Value;

    var failure = nodes[state].Failure;
        var hasFallback = nodes[failure].Next.TryGetValue(c, out var fallback);
        while (failure != 0 && !hasFallback)
    {
     failure = nodes[failure].Failure;
         hasFallback = nodes[failure].Next.TryGetValue(c, out fallback);
    }

        nodes[next].Failure = hasFallback ? fallback : 0;

    if (nodes[nodes[next].Failure].Outputs.Count > 0)
    {
     nodes[next].Outputs.AddRange(nodes[nodes[next].Failure].Outputs);
    }

    queue.Enqueue(next);
   }
  }
 }
}