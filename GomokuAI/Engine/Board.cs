namespace GomokuAI.Engine;

public enum Stone { Empty = 0, Black = 1, White = 2 }

public enum GameMode { PvAI, PvP, AISelfPlay }

public class Move
{
    public int X { get; set; }
    public int Y { get; set; }
    public Stone Player { get; set; }
    public int MoveNumber { get; set; }

    public Move(int x, int y, Stone player, int moveNumber)
    {
        X = x;
        Y = y;
        Player = player;
        MoveNumber = moveNumber;
    }

    public string ToPGN()
    {
        char col = (char)('A' + (X < 8 ? X : X + 1));
        int row = 15 - Y;
        return $"{col}{row}";
    }

    public static Move FromPGN(string pgn, Stone player, int moveNumber)
    {
        if (string.IsNullOrWhiteSpace(pgn) || pgn.Length < 2)
            throw new ArgumentException("Invalid PGN format");

        char colChar = char.ToUpper(pgn[0]);
        int x = colChar - 'A';
        if (x > 7) x--;

        if (!int.TryParse(pgn.Substring(1), out int row))
            throw new ArgumentException("Invalid PGN format");
        int y = 15 - row;

        return new Move(x, y, player, moveNumber);
    }
}

public class Board
{
    public const int Size = 15;
    public Stone[,] Stones { get; private set; }
    public List<Move> MoveHistory { get; private set; }
    public Stone CurrentPlayer { get; private set; }
    public bool GameOver { get; private set; }
    public Stone Winner { get; private set; }
    public List<(int X, int Y)> WinningLine { get; private set; }

    public event EventHandler? BoardChanged;
    public event EventHandler<GameOverEventArgs>? GameOverOccurred;

    public Board()
    {
        Stones = new Stone[Size, Size];
        MoveHistory = new List<Move>();
        CurrentPlayer = Stone.Black;
        GameOver = false;
        Winner = Stone.Empty;
        WinningLine = new List<(int, int)>();
        Reset();
    }

    public void Reset()
    {
        for (int i = 0; i < Size; i++)
            for (int j = 0; j < Size; j++)
                Stones[i, j] = Stone.Empty;

        MoveHistory.Clear();
        CurrentPlayer = Stone.Black;
        GameOver = false;
        Winner = Stone.Empty;
        WinningLine.Clear();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsValidMove(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size) return false;
        if (Stones[x, y] != Stone.Empty) return false;
        if (GameOver) return false;
        return true;
    }

    public bool MakeMove(int x, int y)
    {
        if (!IsValidMove(x, y)) return false;

        Stones[x, y] = CurrentPlayer;
        MoveHistory.Add(new Move(x, y, CurrentPlayer, MoveHistory.Count + 1));

        if (CheckWin(x, y, CurrentPlayer))
        {
            GameOver = true;
            Winner = CurrentPlayer;
            GameOverOccurred?.Invoke(this, new GameOverEventArgs(Winner, WinningLine));
        }
        else if (MoveHistory.Count == Size * Size)
        {
            GameOver = true;
            Winner = Stone.Empty;
            GameOverOccurred?.Invoke(this, new GameOverEventArgs(Stone.Empty, new List<(int, int)>()));
        }
        else
        {
            CurrentPlayer = CurrentPlayer == Stone.Black ? Stone.White : Stone.Black;
        }

        BoardChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool UndoMove()
    {
        if (MoveHistory.Count == 0) return false;

        var lastMove = MoveHistory[^1];
        Stones[lastMove.X, lastMove.Y] = Stone.Empty;
        MoveHistory.RemoveAt(MoveHistory.Count - 1);

        CurrentPlayer = lastMove.Player;
        GameOver = false;
        Winner = Stone.Empty;
        WinningLine.Clear();
        BoardChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Stone GetStone(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size) return Stone.Empty;
        return Stones[x, y];
    }

    private bool CheckWin(int x, int y, Stone player)
    {
        int[] dx = { 1, 0, 1, 1 };
        int[] dy = { 0, 1, 1, -1 };

        for (int dir = 0; dir < 4; dir++)
        {
            var line = new List<(int, int)> { (x, y) };

            for (int step = 1; step < 5; step++)
            {
                int nx = x + dx[dir] * step;
                int ny = y + dy[dir] * step;
                if (nx < 0 || nx >= Size || ny < 0 || ny >= Size) break;
                if (Stones[nx, ny] != player) break;
                line.Add((nx, ny));
            }

            for (int step = 1; step < 5; step++)
            {
                int nx = x - dx[dir] * step;
                int ny = y - dy[dir] * step;
                if (nx < 0 || nx >= Size || ny < 0 || ny >= Size) break;
                if (Stones[nx, ny] != player) break;
                line.Add((nx, ny));
            }

            if (line.Count >= 5)
            {
                WinningLine = line;
                return true;
            }
        }

        return false;
    }

    public List<(int X, int Y)> GetAvailableMoves()
    {
        var moves = new List<(int, int)>();
        bool hasStones = MoveHistory.Count > 0;

        if (!hasStones)
        {
            int center = Size / 2;
            moves.Add((center, center));
            return moves;
        }

        bool[,] candidates = new bool[Size, Size];
        int expandRange = 2;

        foreach (var move in MoveHistory)
        {
            for (int dx = -expandRange; dx <= expandRange; dx++)
            {
                for (int dy = -expandRange; dy <= expandRange; dy++)
                {
                    int nx = move.X + dx;
                    int ny = move.Y + dy;
                    if (nx >= 0 && nx < Size && ny >= 0 && ny < Size && Stones[nx, ny] == Stone.Empty)
                    {
                        candidates[nx, ny] = true;
                    }
                }
            }
        }

        for (int i = 0; i < Size; i++)
            for (int j = 0; j < Size; j++)
                if (candidates[i, j])
                    moves.Add((i, j));

        if (moves.Count == 0)
        {
            for (int i = 0; i < Size; i++)
                for (int j = 0; j < Size; j++)
                    if (Stones[i, j] == Stone.Empty)
                        moves.Add((i, j));
        }

        return moves;
    }

    public Board Clone()
    {
        var clone = new Board();
        clone.Stones = (Stone[,])Stones.Clone();
        clone.MoveHistory = new List<Move>(MoveHistory);
        clone.CurrentPlayer = CurrentPlayer;
        clone.GameOver = GameOver;
        clone.Winner = Winner;
        clone.WinningLine = new List<(int, int)>(WinningLine);
        return clone;
    }
}

public class GameOverEventArgs : EventArgs
{
    public Stone Winner { get; }
    public List<(int X, int Y)> WinningLine { get; }

    public GameOverEventArgs(Stone winner, List<(int X, int Y)> winningLine)
    {
        Winner = winner;
        WinningLine = winningLine;
    }
}
