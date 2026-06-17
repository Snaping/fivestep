using System.Drawing;
using System.Drawing.Drawing2D;
using GomokuAI.Engine;

namespace GomokuAI;

public partial class MainForm : Form
{
    private readonly Board _board;
    private readonly OpeningBook _openingBook;
    private MCTS? _mcts;
    private CancellationTokenSource? _aiCts;
    private Task? _aiTask;

    private GameMode _gameMode;
    private Stone _humanPlayer;
    private Stone _aiPlayer;
    private int _hintCount;
    private List<CandidateMove> _currentCandidates;
    private (int X, int Y)? _hintMove;
    private (int X, int Y)? _hoverCell;
    private System.Windows.Forms.Timer? _thinkTimer;
    private DateTime _thinkStartTime;
    private bool _useTimeLimit;
    private TimeSpan _currentTimeLimit;

    private const int CellSize = 40;
    private const int BoardMargin = 20;
    private const int StoneRadius = 16;

    public MainForm()
    {
        InitializeComponent();
        _board = new Board();
        _openingBook = new OpeningBook();
        _currentCandidates = new List<CandidateMove>();
        _hintCount = 3;
        _gameMode = GameMode.PvAI;
        _humanPlayer = Stone.Black;
        _aiPlayer = Stone.White;
        _useTimeLimit = false;
        _currentTimeLimit = TimeSpan.FromSeconds(5);

        _board.BoardChanged += (s, e) => InvalidateBoard();
        _board.GameOverOccurred += OnGameOver;

        cmbAISide.SelectedIndex = 1;
        UpdateUIState();
    }

    private void InvalidateBoard()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => panelBoard.Invalidate()));
        }
        else
        {
            panelBoard.Invalidate();
        }
    }

    private void panelBoard_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bgBrush = new SolidBrush(Color.FromArgb(222, 184, 135));
        g.FillRectangle(bgBrush, 0, 0, panelBoard.Width, panelBoard.Height);

        var linePen = new Pen(Color.Black, 1);
        for (int i = 0; i < Board.Size; i++)
        {
            g.DrawLine(linePen,
                BoardMargin + i * CellSize, BoardMargin,
                BoardMargin + i * CellSize, BoardMargin + (Board.Size - 1) * CellSize);
            g.DrawLine(linePen,
                BoardMargin, BoardMargin + i * CellSize,
                BoardMargin + (Board.Size - 1) * CellSize, BoardMargin + i * CellSize);
        }

        DrawStarPoints(g);
        DrawBoardLabels(g);
        DrawHoverHighlight(g);
        DrawCandidates(g);
        DrawHintHighlight(g);
        DrawStones(g);
        DrawWinningLine(g);
        DrawMoveNumbers(g);
    }

    private void DrawStarPoints(Graphics g)
    {
        int[] starPoints = { 3, 7, 11 };
        var starBrush = new SolidBrush(Color.Black);
        foreach (var px in starPoints)
        {
            foreach (var py in starPoints)
            {
                int cx = BoardMargin + px * CellSize;
                int cy = BoardMargin + py * CellSize;
                g.FillEllipse(starBrush, cx - 4, cy - 4, 8, 8);
            }
        }
    }

    private void DrawBoardLabels(Graphics g)
    {
        var labelFont = new Font("Consolas", 8);
        var labelBrush = new SolidBrush(Color.DimGray);

        for (int i = 0; i < Board.Size; i++)
        {
            char col = (char)('A' + (i < 8 ? i : i + 1));
            g.DrawString(col.ToString(), labelFont, labelBrush,
                BoardMargin + i * CellSize - 4, 2);
            g.DrawString((Board.Size - i).ToString(), labelFont, labelBrush,
                2, BoardMargin + i * CellSize - 6);
        }
    }

    private void DrawHoverHighlight(Graphics g)
    {
        if (_hoverCell.HasValue && _board.IsValidMove(_hoverCell.Value.X, _hoverCell.Value.Y))
        {
            int cx = BoardMargin + _hoverCell.Value.X * CellSize;
            int cy = BoardMargin + _hoverCell.Value.Y * CellSize;
            var player = _board.CurrentPlayer;
            var hoverColor = player == Stone.Black ? Color.FromArgb(60, Color.Black) : Color.FromArgb(60, Color.White);
            using var brush = new SolidBrush(hoverColor);
            g.FillEllipse(brush, cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2);
        }
    }

    private void DrawCandidates(Graphics g)
    {
        if (_currentCandidates.Count == 0 || _board.GameOver) return;

        for (int i = 0; i < _currentCandidates.Count; i++)
        {
            var c = _currentCandidates[i];
            int cx = BoardMargin + c.X * CellSize;
            int cy = BoardMargin + c.Y * CellSize;

            Color[] colors = { Color.FromArgb(180, Color.LimeGreen), Color.FromArgb(180, Color.Yellow), Color.FromArgb(180, Color.Orange) };
            int colorIdx = Math.Min(i, colors.Length - 1);
            using var pen = new Pen(colors[colorIdx], 3);
            using var brush = new SolidBrush(Color.FromArgb(50, colors[colorIdx]));

            g.FillEllipse(brush, cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2);
            g.DrawEllipse(pen, cx - StoneRadius - 2, cy - StoneRadius - 2, StoneRadius * 2 + 4, StoneRadius * 2 + 4);

            using var rankBrush = new SolidBrush(colors[colorIdx]);
            using var rankFont = new Font("Arial", 9, FontStyle.Bold);
            var rankText = "#" + (i + 1).ToString();
            var sz = g.MeasureString(rankText, rankFont);
            g.FillRectangle(Brushes.White, cx - sz.Width / 2 - 2, cy + StoneRadius + 2, sz.Width + 4, sz.Height);
            g.DrawString(rankText, rankFont, rankBrush, cx - sz.Width / 2, cy + StoneRadius + 3);
        }
    }

    private void DrawHintHighlight(Graphics g)
    {
        if (!_hintMove.HasValue) return;

        int cx = BoardMargin + _hintMove.Value.X * CellSize;
        int cy = BoardMargin + _hintMove.Value.Y * CellSize;

        using var pen = new Pen(Color.Red, 4);

        for (int i = 0; i < 3; i++)
        {
            g.DrawEllipse(pen, cx - StoneRadius - 6 - i * 3, cy - StoneRadius - 6 - i * 3,
                StoneRadius * 2 + 12 + i * 6, StoneRadius * 2 + 12 + i * 6);
        }

        var warnFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        var warnText = "WARN!";
        var sz = g.MeasureString(warnText, warnFont);
        g.FillRectangle(Brushes.Red, cx - sz.Width / 2 - 4, cy - StoneRadius - 35, sz.Width + 8, sz.Height + 4);
        g.DrawString(warnText, warnFont, Brushes.White, cx - sz.Width / 2, cy - StoneRadius - 33);
    }

    private void DrawStones(Graphics g)
    {
        for (int x = 0; x < Board.Size; x++)
        {
            for (int y = 0; y < Board.Size; y++)
            {
                var stone = _board.GetStone(x, y);
                if (stone == Stone.Empty) continue;

                int cx = BoardMargin + x * CellSize;
                int cy = BoardMargin + y * CellSize;

                if (stone == Stone.Black)
                {
                    var gradBrush = new LinearGradientBrush(
                        new Rectangle(cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2),
                        Color.Gray, Color.Black, 45f);
                    g.FillEllipse(gradBrush, cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2);
                    var highlightBrush = new SolidBrush(Color.FromArgb(100, Color.White));
                    g.FillEllipse(highlightBrush, cx - StoneRadius + 4, cy - StoneRadius + 4, 8, 8);
                }
                else
                {
                    var gradBrush = new LinearGradientBrush(
                        new Rectangle(cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2),
                        Color.White, Color.LightGray, 45f);
                    g.FillEllipse(gradBrush, cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2);
                    g.DrawEllipse(Pens.Gray, cx - StoneRadius, cy - StoneRadius, StoneRadius * 2, StoneRadius * 2);
                    var highlightBrush = new SolidBrush(Color.FromArgb(80, Color.White));
                    g.FillEllipse(highlightBrush, cx - StoneRadius + 4, cy - StoneRadius + 4, 8, 8);
                }
            }
        }

        if (_board.MoveHistory.Count > 0 && !_board.GameOver)
        {
            var lastMove = _board.MoveHistory[^1];
            int cx = BoardMargin + lastMove.X * CellSize;
            int cy = BoardMargin + lastMove.Y * CellSize;
            using var pen = new Pen(Color.Red, 2);
            g.DrawRectangle(pen, cx - 4, cy - 4, 8, 8);
        }
    }

    private void DrawWinningLine(Graphics g)
    {
        if (!_board.GameOver || _board.WinningLine.Count == 0) return;

        using var pen = new Pen(Color.Red, 4)
        {
            DashStyle = DashStyle.Dash
        };
        using var brush = new SolidBrush(Color.FromArgb(180, Color.Yellow));

        foreach (var (x, y) in _board.WinningLine)
        {
            int cx = BoardMargin + x * CellSize;
            int cy = BoardMargin + y * CellSize;
            g.FillEllipse(brush, cx - StoneRadius - 4, cy - StoneRadius - 4,
                StoneRadius * 2 + 8, StoneRadius * 2 + 8);
            g.DrawEllipse(pen, cx - StoneRadius - 4, cy - StoneRadius - 4,
                StoneRadius * 2 + 8, StoneRadius * 2 + 8);
        }
    }

    private void DrawMoveNumbers(Graphics g)
    {
        if (_board.MoveHistory.Count == 0) return;

        using var numFont = new Font("Consolas", 8, FontStyle.Bold);
        foreach (var move in _board.MoveHistory)
        {
            int cx = BoardMargin + move.X * CellSize;
            int cy = BoardMargin + move.Y * CellSize;
            var text = move.MoveNumber.ToString();
            var sz = g.MeasureString(text, numFont);

            var textColor = move.Player == Stone.Black ? Color.White : Color.Black;
            g.DrawString(text, numFont, new SolidBrush(textColor),
                cx - sz.Width / 2, cy - sz.Height / 2);
        }
    }

    private void panelBoard_MouseMove(object? sender, MouseEventArgs e)
    {
        var cell = PointToCell(e.X, e.Y);
        if (cell != _hoverCell)
        {
            _hoverCell = cell;
            panelBoard.Invalidate();
        }
    }

    private void panelBoard_MouseClick(object? sender, MouseEventArgs e)
    {
        if (_aiTask != null && !_aiTask.IsCompleted) return;
        if (_board.GameOver) return;

        var cell = PointToCell(e.X, e.Y);
        if (!cell.HasValue) return;

        if (_gameMode == GameMode.PvAI)
        {
            if (_board.CurrentPlayer != _humanPlayer) return;
            if (_board.MakeMove(cell.Value.X, cell.Value.Y))
            {
                _hintMove = null;
                UpdateUIState();
                if (!_board.GameOver)
                {
                    _ = RunAIMove();
                }
            }
        }
        else if (_gameMode == GameMode.PvP)
        {
            if (_board.MakeMove(cell.Value.X, cell.Value.Y))
            {
                _hintMove = null;
                UpdateUIState();
            }
        }
    }

    private (int X, int Y)? PointToCell(int px, int py)
    {
        double fx = (px - BoardMargin) / (double)CellSize;
        double fy = (py - BoardMargin) / (double)CellSize;
        int x = (int)Math.Round(fx);
        int y = (int)Math.Round(fy);

        if (x < 0 || x >= Board.Size || y < 0 || y >= Board.Size) return null;

        int cx = BoardMargin + x * CellSize;
        int cy = BoardMargin + y * CellSize;
        int dist = Math.Abs(px - cx) + Math.Abs(py - cy);
        if (dist > CellSize * 0.7) return null;

        return (x, y);
    }

    private async Task RunAIMove()
    {
        if (_board.CurrentPlayer != _aiPlayer || _board.GameOver) return;

        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        btnStopAI.Enabled = true;
        btnNewGame.Enabled = false;
        btnUndo.Enabled = false;

        _thinkStartTime = DateTime.Now;
        _thinkTimer?.Stop();
        _thinkTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _thinkTimer.Tick += (s, e) => UpdateThinkTimer();
        _thinkTimer.Start();

        var openingMove = chkUseOpeningBook.Checked ? _openingBook.GetBestResponse(_board) : null;

        try
        {
            if (openingMove.HasValue && _board.IsValidMove(openingMove.Value.X, openingMove.Value.Y))
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;

                SetStatus("Opening Book: " + MoveToStr(openingMove.Value.X, openingMove.Value.Y));
                _currentCandidates = new List<CandidateMove>
                {
                    new CandidateMove { X = openingMove.Value.X, Y = openingMove.Value.Y, WinRate = 1.0, Visits = 0 }
                };
                UpdateCandidateLabels();
                progressBar.Value = 100;
                lblProgress.Text = "Progress: 100% (Book)";
                await Task.Delay(200, token);
            }
            else
            {
                _mcts = new MCTS(_board, _aiPlayer);
                _mcts.ProgressChanged += OnAIProgress;
                _mcts.CandidatesUpdated += OnAICandidates;

                int sims = (int)numSimulations.Value;
                var timeLimit = TimeSpan.FromSeconds((double)numTimeLimit.Value);
                _useTimeLimit = rbTimeLimit.Checked;
                _currentTimeLimit = timeLimit;

                SetStatus(_useTimeLimit
                    ? "AI thinking... (Time limit " + timeLimit.TotalSeconds + "s)"
                    : "AI thinking... (" + sims.ToString("N0") + " simulations)");

                var aiTask = Task.Run(() => _useTimeLimit
                    ? _mcts.Search(timeLimit: timeLimit, cancellationToken: token)
                    : _mcts.Search(simulations: sims, cancellationToken: token), token);

                var move = await aiTask;
                openingMove = move;
            }

            if (!token.IsCancellationRequested && openingMove.HasValue &&
                _board.IsValidMove(openingMove.Value.X, openingMove.Value.Y))
            {
                _board.MakeMove(openingMove.Value.X, openingMove.Value.Y);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("AI thinking cancelled");
        }
        catch (Exception ex)
        {
            SetStatus("AI error: " + ex.Message);
        }
        finally
        {
            _thinkTimer?.Stop();
            _aiTask = null;
            _aiCts?.Dispose();
            _aiCts = null;

            btnStopAI.Enabled = false;
            btnNewGame.Enabled = true;
            btnUndo.Enabled = _board.MoveHistory.Count > 0;
            UpdateUIState();

            if (_gameMode == GameMode.AISelfPlay && !_board.GameOver)
            {
                await Task.Delay(500);
                _ = RunAIMove();
            }
        }
    }

    private void UpdateThinkTimer()
    {
        var elapsed = DateTime.Now - _thinkStartTime;
        string timeStr;

        if (_useTimeLimit)
        {
            var remaining = _currentTimeLimit - elapsed;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            timeStr = "[X] Remaining: " + remaining.ToString(@"ss\.ff") + "s | Elapsed: " + elapsed.ToString(@"ss\.ff") + "s";
        }
        else
        {
            timeStr = "[X] Thinking: " + elapsed.ToString(@"ss\.ff") + "s";
        }

        if (lblTimer != null && !lblTimer.IsDisposed)
        {
            lblTimer.Text = timeStr;
        }
    }

    private void OnAIProgress(object? sender, int progress)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnAIProgress(sender, progress)));
            return;
        }
        progressBar.Value = Math.Clamp(progress, 0, 100);
        lblProgress.Text = "Progress: " + progress + "%";
    }

    private void OnAICandidates(object? sender, List<CandidateMove> candidates)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnAICandidates(sender, candidates)));
            return;
        }
        _currentCandidates = candidates;
        UpdateCandidateLabels();
        InvalidateBoard();
    }

    private void UpdateCandidateLabels()
    {
        lblCandidate1.Text = _currentCandidates.Count > 0
            ? "1. " + MoveToStr(_currentCandidates[0].X, _currentCandidates[0].Y).PadRight(8) + " " + (_currentCandidates[0].WinRate * 100).ToString("F1").PadLeft(5) + "% (N=" + _currentCandidates[0].Visits.ToString("N0") + ")"
            : "1. --        0.0%";
        lblCandidate2.Text = _currentCandidates.Count > 1
            ? "2. " + MoveToStr(_currentCandidates[1].X, _currentCandidates[1].Y).PadRight(8) + " " + (_currentCandidates[1].WinRate * 100).ToString("F1").PadLeft(5) + "% (N=" + _currentCandidates[1].Visits.ToString("N0") + ")"
            : "2. --        0.0%";
        lblCandidate3.Text = _currentCandidates.Count > 2
            ? "3. " + MoveToStr(_currentCandidates[2].X, _currentCandidates[2].Y).PadRight(8) + " " + (_currentCandidates[2].WinRate * 100).ToString("F1").PadLeft(5) + "% (N=" + _currentCandidates[2].Visits.ToString("N0") + ")"
            : "3. --        0.0%";
    }

    private string MoveToStr(int x, int y)
    {
        char col = (char)('A' + (x < 8 ? x : x + 1));
        int row = 15 - y;
        return col + row.ToString();
    }

    private void OnGameOver(object? sender, GameOverEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnGameOver(sender, e)));
            return;
        }

        _thinkTimer?.Stop();
        string result;
        if (e.Winner == Stone.Black) result = "Black wins! Five in a row";
        else if (e.Winner == Stone.White) result = "White wins! Five in a row";
        else result = "Draw - Board full";

        SetStatus(result);
        lblTimer.Text = "[X] Game Over";
        InvalidateBoard();
        MessageBox.Show(result, "Game Over", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UpdateUIState()
    {
        lblCurrentPlayer.Text = _board.CurrentPlayer == Stone.Black ? "Black" : "White";
        lblCurrentPlayer.ForeColor = _board.CurrentPlayer == Stone.Black ? Color.Black : Color.DimGray;
        lblMoveCount.Text = _board.MoveHistory.Count.ToString();
        lblHintCount.Text = "Hints left: " + _hintCount.ToString();
        btnHint.Enabled = _hintCount > 0 && _gameMode == GameMode.PvAI &&
                          _board.CurrentPlayer == _humanPlayer && !_board.GameOver;

        if (!_board.GameOver)
        {
            if (_gameMode == GameMode.AISelfPlay)
            {
                SetStatus("AI Self-Play in progress...");
            }
            else if (_gameMode == GameMode.PvAI)
            {
                string side = _board.CurrentPlayer == _humanPlayer ? "Player" : "AI";
                string who = _board.CurrentPlayer == Stone.Black ? "Black" : "White";
                SetStatus(who + "'s turn (" + side + ") - Move " + (_board.MoveHistory.Count + 1).ToString());
            }
            else
            {
                string who = _board.CurrentPlayer == Stone.Black ? "Black" : "White";
                SetStatus(who + "'s turn - Move " + (_board.MoveHistory.Count + 1).ToString());
            }
        }

        btnUndo.Enabled = _board.MoveHistory.Count > 0 && (_aiTask == null || _aiTask.IsCompleted);
    }

    private void SetStatus(string msg)
    {
        if (lblStatus != null && !lblStatus.IsDisposed)
        {
            if (InvokeRequired) Invoke(new Action(() => lblStatus.Text = msg));
            else lblStatus.Text = msg;
        }
    }

    private void btnNewGame_Click(object? sender, EventArgs e)
    {
        _aiCts?.Cancel();
        try { _aiTask?.Wait(1000); } catch { }
        _thinkTimer?.Stop();

        _gameMode = rbPvAI.Checked ? GameMode.PvAI : rbPvP.Checked ? GameMode.PvP : GameMode.AISelfPlay;
        _aiPlayer = cmbAISide.SelectedIndex == 0 ? Stone.Black : Stone.White;
        _humanPlayer = _aiPlayer == Stone.Black ? Stone.White : Stone.Black;
        _hintCount = 3;
        _hintMove = null;
        _currentCandidates.Clear();
        UpdateCandidateLabels();
        progressBar.Value = 0;
        lblProgress.Text = "Progress: 0%";
        lblTimer.Text = "[X] Time remaining: --";

        _board.Reset();
        UpdateUIState();

        if (_gameMode == GameMode.AISelfPlay || (_gameMode == GameMode.PvAI && _board.CurrentPlayer == _aiPlayer))
        {
            _ = RunAIMove();
        }
    }

    private void btnUndo_Click(object? sender, EventArgs e)
    {
        if (_aiTask != null && !_aiTask.IsCompleted) return;

        int undoCount = _gameMode == GameMode.PvAI ? 2 : 1;
        for (int i = 0; i < undoCount && _board.MoveHistory.Count > 0; i++)
        {
            _board.UndoMove();
        }

        _hintMove = null;
        _currentCandidates.Clear();
        UpdateCandidateLabels();
        UpdateUIState();
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PGN Files (*.pgn)|*.pgn|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = "gomoku_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pgn",
            DefaultExt = ".pgn"
        };

        if (dlg.ShowDialog() == DialogResult.OK)
        {
            try
            {
                string wp = _gameMode == GameMode.PvAI
                    ? (_aiPlayer == Stone.White ? "AI" : "Human")
                    : _gameMode == GameMode.PvP ? "Player2" : "AI-White";
                string bp = _gameMode == GameMode.PvAI
                    ? (_aiPlayer == Stone.Black ? "AI" : "Human")
                    : _gameMode == GameMode.PvP ? "Player1" : "AI-Black";

                PGNManager.SaveToFile(_board, dlg.FileName, "Gomoku Game", wp, bp);
                SetStatus("Saved to: " + Path.GetFileName(dlg.FileName));
                MessageBox.Show("Save successful!", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void btnLoad_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "PGN Files (*.pgn)|*.pgn|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == DialogResult.OK)
        {
            try
            {
                _aiCts?.Cancel();
                var loadedBoard = PGNManager.LoadFromFile(dlg.FileName);

                _board.Reset();
                foreach (var move in loadedBoard.MoveHistory)
                {
                    _board.MakeMove(move.X, move.Y);
                    if (_board.GameOver) break;
                }

                _hintMove = null;
                _currentCandidates.Clear();
                UpdateCandidateLabels();
                UpdateUIState();
                SetStatus("Loaded: " + Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Load failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async void btnHint_Click(object? sender, EventArgs e)
    {
        if (_hintCount <= 0 || _board.CurrentPlayer != _humanPlayer || _board.GameOver) return;
        if (_aiTask != null && !_aiTask.IsCompleted) return;

        _hintCount--;
        btnHint.Enabled = false;
        SetStatus("AI analyzing best move...");
        progressBar.Value = 0;

        var hintCts = new CancellationTokenSource();
        try
        {
            _mcts = new MCTS(_board, _aiPlayer);
            _mcts.ProgressChanged += OnAIProgress;
            int sims = Math.Min(1000, (int)numSimulations.Value / 2);

            var (x, y) = await Task.Run(() => _mcts.Search(simulations: sims, cancellationToken: hintCts.Token));

            if (x >= 0 && y >= 0)
            {
                _hintMove = (x, y);
                SetStatus("Hint! AI suggests: " + MoveToStr(x, y));
                InvalidateBoard();
            }
        }
        catch { }
        finally { hintCts.Dispose(); UpdateUIState(); }
    }

    private void btnStopAI_Click(object? sender, EventArgs e)
    {
        _aiCts?.Cancel();
        btnStopAI.Enabled = false;
        SetStatus("Stopping AI...");
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _aiCts?.Cancel();
        _thinkTimer?.Stop();
        try { _aiTask?.Wait(500); } catch { }
    }
}
