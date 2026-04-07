namespace UnbsAttention.Services;

public static class CsvReader
{
 public static List<IReadOnlyList<string>> Parse(string csv)
 {
  var result = new List<IReadOnlyList<string>>();
  var row = new List<string>();
  var cell = new System.Text.StringBuilder();
  var inQuotes = false;

  for (var i = 0; i < csv.Length; i++)
  {
   var c = csv[i];

   if (inQuotes)
   {
    if (c == '"')
    {
     if (i + 1 < csv.Length && csv[i + 1] == '"')
     {
      cell.Append('"');
      i++;
     }
     else
     {
      inQuotes = false;
     }
    }
    else
    {
     cell.Append(c);
    }

    continue;
   }

   if (c == '"')
   {
    inQuotes = true;
    continue;
   }

   if (c == ',')
   {
    row.Add(cell.ToString());
    cell.Clear();
    continue;
   }

   if (c == '\r')
   {
    continue;
   }

   if (c == '\n')
   {
    row.Add(cell.ToString());
    cell.Clear();
    result.Add(row);
    row = new List<string>();
    continue;
   }

   cell.Append(c);
  }

  if (cell.Length > 0 || row.Count > 0)
  {
   row.Add(cell.ToString());
   result.Add(row);
  }

  return result;
 }
}
