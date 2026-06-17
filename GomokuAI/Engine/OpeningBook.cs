namespace GomokuAI.Engine;

public class OpeningBook
{
    private Dictionary<string, List<(int X, int Y, int Weight)>> _book;

    public OpeningBook()
    {
        _book = new Dictionary<string, List<(int, int, int)>>();
        InitializeStandardOpenings();
    }

    private void InitializeStandardOpenings()
    {
        var center = Board.Size / 2;

        AddOpening(new List<(int, int)> { (center, center) },
            new List<(int, int, int)>
            {
                (center - 1, center, 10),
                (center + 1, center, 10),
                (center, center - 1, 10),
                (center, center + 1, 10),
                (center - 2, center, 5),
                (center + 2, center, 5),
                (center - 1, center - 1, 8),
                (center + 1, center + 1, 8)
            });

        AddOpening(new List<(int, int)> { (center, center), (center - 1, center) },
            new List<(int, int, int)>
            {
                (center + 1, center, 15),
                (center - 1, center - 1, 12),
                (center - 1, center + 1, 12),
                (center, center - 1, 10),
                (center, center + 1, 10)
            });

        AddOpening(new List<(int, int)> { (center, center), (center, center - 1) },
            new List<(int, int, int)>
            {
                (center, center + 1, 15),
                (center - 1, center - 1, 12),
                (center + 1, center - 1, 12),
                (center - 1, center, 10),
                (center + 1, center, 10)
            });

        AddOpening(new List<(int, int)> { (center, center), (center - 1, center - 1) },
            new List<(int, int, int)>
            {
                (center + 1, center + 1, 15),
                (center - 2, center - 2, 12),
                (center + 2, center, 10),
                (center, center + 2, 10),
                (center - 1, center, 8),
                (center, center - 1, 8)
            });

        AddOpening(new List<(int, int)> { (center, center), (center + 1, center + 1) },
            new List<(int, int, int)>
            {
                (center - 1, center - 1, 15),
                (center + 2, center + 2, 12),
                (center - 2, center, 10),
                (center, center - 2, 10),
                (center + 1, center, 8),
                (center, center + 1, 8)
            });

        AddOpening(new List<(int, int)> { (center, center), (center + 1, center) },
            new List<(int, int, int)>
            {
                (center - 1, center, 15),
                (center + 1, center - 1, 12),
                (center + 1, center + 1, 12),
                (center, center - 1, 10),
                (center, center + 1, 10)
            });

        AddOpening(new List<(int, int)> { (center, center), (center, center + 1) },
            new List<(int, int, int)>
            {
                (center, center - 1, 15),
                (center - 1, center + 1, 12),
                (center + 1, center + 1, 12),
                (center - 1, center, 10),
                (center + 1, center, 10)
            });
    }

    private void AddOpening(List<(int X, int Y)> moveSequence, List<(int X, int Y, int Weight)> responses)
    {
        var key = GenerateKey(moveSequence);
        _book[key] = responses;
    }

    private string GenerateKey(List<(int X, int Y)> moves)
    {
        var parts = moves.Select(m => $"{m.X},{m.Y}");
        return string.Join("|", parts);
    }

    public (int X, int Y)? GetBestResponse(Board board)
    {
        if (board.MoveHistory.Count > 6) return null;

        var moveSequence = board.MoveHistory.Select(m => (m.X, m.Y)).ToList();

        while (moveSequence.Count > 0)
        {
            var key = GenerateKey(moveSequence);
            if (_book.TryGetValue(key, out var responses))
            {
                var validResponses = responses.Where(r => board.IsValidMove(r.X, r.Y)).ToList();
                if (validResponses.Count > 0)
                {
                    int totalWeight = validResponses.Sum(r => r.Weight);
                    int rand = new Random(Guid.NewGuid().GetHashCode()).Next(totalWeight);
                    int current = 0;
                    foreach (var resp in validResponses)
                    {
                        current += resp.Weight;
                        if (rand < current)
                        {
                            return (resp.X, resp.Y);
                        }
                    }
                    return (validResponses[0].X, validResponses[0].Y);
                }
            }
            moveSequence.RemoveAt(moveSequence.Count - 1);
        }

        return null;
    }

    public bool HasOpeningMove(Board board)
    {
        return GetBestResponse(board).HasValue;
    }
}
