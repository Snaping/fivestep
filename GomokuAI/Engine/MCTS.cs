namespace GomokuAI.Engine;

public class MCTSNode
{
    public MCTSNode? Parent { get; set; }
    public List<MCTSNode> Children { get; set; }
    public int MoveX { get; set; }
    public int MoveY { get; set; }
    public Stone Player { get; set; }
    public int Visits { get; set; }
    public double Wins { get; set; }
    public bool IsFullyExpanded { get; set; }
    public List<(int X, int Y)>? UntriedMoves { get; set; }

    public MCTSNode(MCTSNode? parent, int moveX, int moveY, Stone player)
    {
        Parent = parent;
        Children = new List<MCTSNode>();
        MoveX = moveX;
        MoveY = moveY;
        Player = player;
        Visits = 0;
        Wins = 0;
        IsFullyExpanded = false;
    }

    public double UCT(double explorationParam = 1.414)
    {
        if (Visits == 0) return double.MaxValue;
        return (Wins / Visits) + explorationParam * Math.Sqrt(Math.Log(Parent!.Visits) / Visits);
    }
}

public class MCTS
{
    private readonly Board _board;
    private readonly Stone _aiPlayer;
    private readonly Random _random;
    private CancellationTokenSource? _cts;
    private int _simulations;
    private TimeSpan _timeLimit;
    private bool _useTimeLimit;

    public int TotalSimulations { get; private set; }
    public int CompletedSimulations { get; private set; }
    public event EventHandler<int>? ProgressChanged;
    public event EventHandler<List<CandidateMove>>? CandidatesUpdated;

    public MCTS(Board board, Stone aiPlayer)
    {
        _board = board;
        _aiPlayer = aiPlayer;
        _random = new Random(Guid.NewGuid().GetHashCode());
    }

    public void Configure(int simulations)
    {
        _simulations = simulations;
        _useTimeLimit = false;
    }

    public void Configure(TimeSpan timeLimit)
    {
        _timeLimit = timeLimit;
        _useTimeLimit = true;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public (int X, int Y) Search(int simulations = 1000, TimeSpan? timeLimit = null, CancellationToken? cancellationToken = null)
    {
        if (timeLimit.HasValue) Configure(timeLimit.Value);
        else Configure(simulations);

        return Search(cancellationToken);
    }

    public (int X, int Y) Search(CancellationToken? externalToken = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken ?? CancellationToken.None);
        var token = _cts.Token;

        TotalSimulations = _useTimeLimit ? 100000 : _simulations;
        CompletedSimulations = 0;

        var root = new MCTSNode(null, -1, -1, _aiPlayer == Stone.Black ? Stone.White : Stone.Black);
        var boardClone = _board.Clone();
        root.UntriedMoves = boardClone.GetAvailableMoves();

        int progressInterval = Math.Max(1, TotalSimulations / 100);
        int candidateUpdateInterval = Math.Max(1, TotalSimulations / 20);

        DateTime startTime = DateTime.Now;

        try
        {
            if (_useTimeLimit)
            {
                while ((DateTime.Now - startTime) < _timeLimit && !token.IsCancellationRequested)
                {
                    var simulationBoard = _board.Clone();
                    var node = Select(root, simulationBoard);
                    node = Expand(node, simulationBoard);
                    var result = Simulate(simulationBoard, token);
                    Backpropagate(node, result);

                    CompletedSimulations++;
                    if (CompletedSimulations % progressInterval == 0)
                    {
                        ProgressChanged?.Invoke(this, Math.Min(100, (int)((DateTime.Now - startTime).TotalMilliseconds * 100 / _timeLimit.TotalMilliseconds)));
                    }
                    if (CompletedSimulations % candidateUpdateInterval == 0)
                    {
                        CandidatesUpdated?.Invoke(this, GetTopCandidates(root, 3));
                    }
                }
                ProgressChanged?.Invoke(this, 100);
            }
            else
            {
                for (int i = 0; i < _simulations; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var simulationBoard = _board.Clone();
                    var node = Select(root, simulationBoard);
                    node = Expand(node, simulationBoard);
                    var result = Simulate(simulationBoard, token);
                    Backpropagate(node, result);

                    CompletedSimulations = i + 1;
                    if ((i + 1) % progressInterval == 0)
                    {
                        ProgressChanged?.Invoke(this, (i + 1) * 100 / _simulations);
                    }
                    if ((i + 1) % candidateUpdateInterval == 0)
                    {
                        CandidatesUpdated?.Invoke(this, GetTopCandidates(root, 3));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        CandidatesUpdated?.Invoke(this, GetTopCandidates(root, 3));

        MCTSNode? bestChild = null;
        double bestVisits = -1;
        foreach (var child in root.Children)
        {
            if (child.Visits > bestVisits)
            {
                bestVisits = child.Visits;
                bestChild = child;
            }
        }

        if (bestChild == null)
        {
            var moves = _board.GetAvailableMoves();
            if (moves.Count > 0) return moves[_random.Next(moves.Count)];
            return (-1, -1);
        }

        return (bestChild.MoveX, bestChild.MoveY);
    }

    public List<CandidateMove> GetTopCandidates(MCTSNode root, int topN)
    {
        var candidates = new List<CandidateMove>();
        foreach (var child in root.Children)
        {
            double winRate = child.Visits > 0 ? (child.Wins / child.Visits) : 0;
            if (_aiPlayer == Stone.White) winRate = 1 - winRate;
            candidates.Add(new CandidateMove
            {
                X = child.MoveX,
                Y = child.MoveY,
                WinRate = winRate,
                Visits = child.Visits
            });
        }
        return candidates.OrderByDescending(c => c.WinRate).Take(topN).ToList();
    }

    private MCTSNode Select(MCTSNode node, Board board)
    {
        while (!node.IsFullyExpanded && node.Children.Count > 0)
        {
            MCTSNode? bestChild = null;
            double bestUCT = double.MinValue;

            foreach (var child in node.Children)
            {
                double uct = child.UCT();
                if (uct > bestUCT)
                {
                    bestUCT = uct;
                    bestChild = child;
                }
            }

            if (bestChild == null) break;
            board.MakeMove(bestChild.MoveX, bestChild.MoveY);
            node = bestChild;
        }
        return node;
    }

    private MCTSNode Expand(MCTSNode node, Board board)
    {
        if (board.GameOver) return node;

        if (node.UntriedMoves == null || node.UntriedMoves.Count == 0)
        {
            node.UntriedMoves = board.GetAvailableMoves();
        }

        if (node.UntriedMoves.Count == 0)
        {
            node.IsFullyExpanded = true;
            return node;
        }

        int index = _random.Next(node.UntriedMoves.Count);
        var move = node.UntriedMoves[index];
        node.UntriedMoves.RemoveAt(index);

        if (node.UntriedMoves.Count == 0) node.IsFullyExpanded = true;

        var child = new MCTSNode(node, move.X, move.Y, board.CurrentPlayer)
        {
            UntriedMoves = new List<(int, int)>()
        };
        node.Children.Add(child);

        board.MakeMove(move.X, move.Y);
        return child;
    }

    private Stone Simulate(Board board, CancellationToken token)
    {
        int maxSteps = Board.Size * Board.Size * 2;
        int steps = 0;

        while (!board.GameOver && steps < maxSteps)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException();

            var moves = board.GetAvailableMoves();
            if (moves.Count == 0) break;

            var scoredMoves = new List<(int X, int Y, int Score)>();
            foreach (var move in moves)
            {
                int score = HeuristicScore(board, move.X, move.Y, board.CurrentPlayer);
                scoredMoves.Add((move.X, move.Y, score));
            }

            scoredMoves.Sort((a, b) => b.Score.CompareTo(a.Score));
            int topCount = Math.Min(5, scoredMoves.Count);
            var chosen = scoredMoves[_random.Next(topCount)];

            board.MakeMove(chosen.X, chosen.Y);
            steps++;
        }

        return board.Winner;
    }

    private int HeuristicScore(Board board, int x, int y, Stone player)
    {
        int score = 0;
        Stone opponent = player == Stone.Black ? Stone.White : Stone.Black;

        int[] dx = { 1, 0, 1, 1 };
        int[] dy = { 0, 1, 1, -1 };

        for (int dir = 0; dir < 4; dir++)
        {
            int myCount = 1;
            int myOpenEnds = 0;
            int oppCount = 0;
            int oppOpenEnds = 0;

            for (int step = 1; step < 5; step++)
            {
                int nx = x + dx[dir] * step;
                int ny = y + dy[dir] * step;
                if (nx < 0 || nx >= Board.Size || ny < 0 || ny >= Board.Size) break;
                var s = board.GetStone(nx, ny);
                if (s == player) myCount++;
                else if (s == Stone.Empty) { myOpenEnds++; break; }
                else { oppCount++; break; }
            }
            for (int step = 1; step < 5; step++)
            {
                int nx = x - dx[dir] * step;
                int ny = y - dy[dir] * step;
                if (nx < 0 || nx >= Board.Size || ny < 0 || ny >= Board.Size) break;
                var s = board.GetStone(nx, ny);
                if (s == player) myCount++;
                else if (s == Stone.Empty) { myOpenEnds++; break; }
                else { oppCount++; break; }
            }

            if (myCount >= 5) score += 100000;
            else if (myCount == 4 && myOpenEnds >= 1) score += 10000;
            else if (myCount == 3 && myOpenEnds == 2) score += 1000;
            else if (myCount == 3 && myOpenEnds == 1) score += 100;
            else if (myCount == 2 && myOpenEnds == 2) score += 50;

            if (oppCount >= 4) score += 9000;
            else if (oppCount == 3 && oppOpenEnds >= 1) score += 800;
            else if (oppCount == 2 && oppOpenEnds == 2) score += 40;
        }

        int centerDist = Math.Abs(x - Board.Size / 2) + Math.Abs(y - Board.Size / 2);
        score += (Board.Size - centerDist);

        return score;
    }

    private void Backpropagate(MCTSNode node, Stone winner)
    {
        MCTSNode? current = node;
        while (current != null)
        {
            current.Visits++;
            if (winner == Stone.Empty)
            {
                current.Wins += 0.5;
            }
            else if (winner != current.Player)
            {
                current.Wins += 1.0;
            }
            current = current.Parent;
        }
    }
}

public class CandidateMove
{
    public int X { get; set; }
    public int Y { get; set; }
    public double WinRate { get; set; }
    public int Visits { get; set; }
}
