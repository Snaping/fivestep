using System.Text;

namespace GomokuAI.Engine;

public class PGNManager
{
    public static string ToPGN(Board board, string eventName = "Gomoku Game",
        string whitePlayer = "Player1", string blackPlayer = "Player2")
    {
        var sb = new StringBuilder();

        sb.AppendLine($"[Event \"{eventName}\"]");
        sb.AppendLine($"[Site \"GomokuAI\"]");
        sb.AppendLine($"[Date \"{DateTime.Now:yyyy.MM.dd}\"]");
        sb.AppendLine($"[Round \"1\"]");
        sb.AppendLine($"[White \"{whitePlayer}\"]");
        sb.AppendLine($"[Black \"{blackPlayer}\"]");

        string result = board.GameOver switch
        {
            true when board.Winner == Stone.White => "1-0",
            true when board.Winner == Stone.Black => "0-1",
            true => "1/2-1/2",
            _ => "*"
        };
        sb.AppendLine($"[Result \"{result}\"]");
        sb.AppendLine($"[BoardSize \"{Board.Size}\"]");
        sb.AppendLine();

        for (int i = 0; i < board.MoveHistory.Count; i += 2)
        {
            int moveNum = (i / 2) + 1;
            var blackMove = board.MoveHistory[i];
            string whiteMoveStr = i + 1 < board.MoveHistory.Count ? board.MoveHistory[i + 1].ToPGN() : "";
            sb.Append($"{moveNum}. {blackMove.ToPGN()} {whiteMoveStr} ");

            if ((i / 2 + 1) % 6 == 0) sb.AppendLine();
        }

        if (board.GameOver)
        {
            sb.Append(result);
        }

        return sb.ToString();
    }

    public static Board FromPGN(string pgn)
    {
        var board = new Board();
        var lines = pgn.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var moveTokens = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith('[')) continue;

            var tokens = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.EndsWith('.') || token == "*" || token == "1-0" || token == "0-1" || token == "1/2-1/2")
                    continue;
                moveTokens.Add(token);
            }
        }

        int moveNum = 1;
        foreach (var token in moveTokens)
        {
            try
            {
                Stone player = moveNum % 2 == 1 ? Stone.Black : Stone.White;
                var move = Move.FromPGN(token, player, moveNum);
                board.MakeMove(move.X, move.Y);
                moveNum++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid move '{token}': {ex.Message}");
            }
        }

        return board;
    }

    public static void SaveToFile(Board board, string filePath, string eventName = "Gomoku Game",
        string whitePlayer = "Player1", string blackPlayer = "Player2")
    {
        var pgn = ToPGN(board, eventName, whitePlayer, blackPlayer);
        File.WriteAllText(filePath, pgn, Encoding.UTF8);
    }

    public static Board LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PGN file not found", filePath);

        var pgn = File.ReadAllText(filePath, Encoding.UTF8);
        return FromPGN(pgn);
    }

    public static List<Move> ParseMoves(string pgn)
    {
        var moves = new List<Move>();
        var lines = pgn.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var moveTokens = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('[')) continue;

            var tokens = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.EndsWith('.') || token == "*" || token == "1-0" || token == "0-1" || token == "1/2-1/2")
                    continue;
                moveTokens.Add(token);
            }
        }

        int moveNum = 1;
        foreach (var token in moveTokens)
        {
            Stone player = moveNum % 2 == 1 ? Stone.Black : Stone.White;
            moves.Add(Move.FromPGN(token, player, moveNum));
            moveNum++;
        }

        return moves;
    }
}
