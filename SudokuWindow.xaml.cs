using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CabinetFaceWpf;

public partial class SudokuWindow : Window
{
    private const int BoardSize = 9;
    private const int ClueCount = 39;
    private static readonly Random Random = new();
    private readonly int[,] _solution = new int[BoardSize, BoardSize];
    private readonly int[,] _puzzle = new int[BoardSize, BoardSize];
    private readonly int[,] _values = new int[BoardSize, BoardSize];
    private readonly bool[,] _fixedCells = new bool[BoardSize, BoardSize];
    private readonly TextBlock[,] _cellTexts = new TextBlock[BoardSize, BoardSize];
    private readonly Border[,] _cellBorders = new Border[BoardSize, BoardSize];
    private int _selectedRow = -1;
    private int _selectedColumn = -1;

    public SudokuWindow()
    {
        InitializeComponent();
        BuildBoard();
        StartNewGame();
    }

    private void BuildBoard()
    {
        for (int index = 0; index < BoardSize; index++)
        {
            BoardGrid.RowDefinitions.Add(new RowDefinition());
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                var text = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14
                };
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x53, 0x61)),
                    BorderThickness = new Thickness(
                        column % 3 == 0 ? 1 : 0.5,
                        row % 3 == 0 ? 1 : 0.5,
                        column == 8 ? 1 : 0.5,
                        row == 8 ? 1 : 0.5),
                    Background = GetCellBackground(row, column),
                    Child = text,
                    Tag = (row, column),
                    Cursor = Cursors.Hand
                };
                cell.MouseLeftButtonUp += Cell_MouseLeftButtonUp;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                BoardGrid.Children.Add(cell);
                _cellTexts[row, column] = text;
                _cellBorders[row, column] = cell;
            }
        }
    }

    private void StartNewGame()
    {
        GenerateSolution(_solution);
        Array.Copy(_solution, _puzzle, _solution.Length);

        int removed = 0;
        foreach (int cellIndex in GetShuffledIndexes())
        {
            if (removed == BoardSize * BoardSize - ClueCount)
            {
                break;
            }

            int row = cellIndex / BoardSize;
            int column = cellIndex % BoardSize;
            int value = _puzzle[row, column];
            _puzzle[row, column] = 0;

            if (CountSolutions(_puzzle) == 1)
            {
                removed++;
            }
            else
            {
                _puzzle[row, column] = value;
            }
        }

        Array.Copy(_puzzle, _values, _puzzle.Length);
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                _fixedCells[row, column] = _puzzle[row, column] != 0;
            }
        }

        _selectedRow = -1;
        _selectedColumn = -1;
        txtStatus.Text = "点击空格，再选数字";
        RefreshBoard();
    }

    private static void GenerateSolution(int[,] solution)
    {
        int[] digits = Shuffle(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        int[] bands = Shuffle(new[] { 0, 1, 2 });
        int[] stacks = Shuffle(new[] { 0, 1, 2 });
        int[] rows = new int[BoardSize];
        int[] columns = new int[BoardSize];

        for (int band = 0; band < 3; band++)
        {
            int[] withinBand = Shuffle(new[] { 0, 1, 2 });
            for (int row = 0; row < 3; row++)
            {
                rows[band * 3 + row] = bands[band] * 3 + withinBand[row];
            }

            int[] withinStack = Shuffle(new[] { 0, 1, 2 });
            for (int column = 0; column < 3; column++)
            {
                columns[band * 3 + column] = stacks[band] * 3 + withinStack[column];
            }
        }

        bool transpose = Random.Next(2) == 0;
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                int sourceRow = transpose ? columns[column] : rows[row];
                int sourceColumn = transpose ? rows[row] : columns[column];
                int pattern = (sourceRow * 3 + sourceRow / 3 + sourceColumn) % BoardSize;
                solution[row, column] = digits[pattern];
            }
        }
    }

    private static int CountSolutions(int[,] board)
    {
        int bestRow = -1;
        int bestColumn = -1;
        int bestMask = 0;
        int bestCount = BoardSize + 1;

        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                if (board[row, column] != 0)
                {
                    continue;
                }

                int mask = GetCandidateMask(board, row, column);
                int candidateCount = CountBits(mask);
                if (candidateCount < bestCount)
                {
                    bestRow = row;
                    bestColumn = column;
                    bestMask = mask;
                    bestCount = candidateCount;
                    if (candidateCount <= 1)
                    {
                        break;
                    }
                }
            }

            if (bestCount <= 1)
            {
                break;
            }
        }

        if (bestRow == -1)
        {
            return 1;
        }

        if (bestCount == 0)
        {
            return 0;
        }

        int solutions = 0;
        for (int digit = 1; digit <= BoardSize; digit++)
        {
            if ((bestMask & (1 << digit)) == 0)
            {
                continue;
            }

            board[bestRow, bestColumn] = digit;
            solutions += CountSolutions(board);
            board[bestRow, bestColumn] = 0;
            if (solutions > 1)
            {
                return solutions;
            }
        }

        return solutions;
    }

    private static int GetCandidateMask(int[,] board, int row, int column)
    {
        int used = 0;
        for (int index = 0; index < BoardSize; index++)
        {
            used |= 1 << board[row, index];
            used |= 1 << board[index, column];
        }

        int boxRow = row / 3 * 3;
        int boxColumn = column / 3 * 3;
        for (int boxR = boxRow; boxR < boxRow + 3; boxR++)
        {
            for (int boxC = boxColumn; boxC < boxColumn + 3; boxC++)
            {
                used |= 1 << board[boxR, boxC];
            }
        }

        return (~used) & 0x3FE;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static int[] GetShuffledIndexes()
    {
        return Shuffle(System.Linq.Enumerable.Range(0, BoardSize * BoardSize).ToArray());
    }

    private static int[] Shuffle(int[] values)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swapIndex = Random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }

        return values;
    }

    private static Brush GetCellBackground(int row, int column)
    {
        return (row / 3 + column / 3) % 2 == 0
            ? new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x31))
            : new SolidColorBrush(Color.FromRgb(0x28, 0x2E, 0x39));
    }

    private void Cell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: ValueTuple<int, int> position })
        {
            _selectedRow = position.Item1;
            _selectedColumn = position.Item2;
            RefreshBoard();
        }
    }

    private void NumberButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digitText } && int.TryParse(digitText, out int digit))
        {
            SetSelectedValue(digit);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedValue(0);
    }

    private void SetSelectedValue(int value)
    {
        if (_selectedRow < 0 || _selectedColumn < 0 || _fixedCells[_selectedRow, _selectedColumn])
        {
            txtStatus.Text = "先选择一个空格";
            return;
        }

        if (value != 0 && HasDuplicate(_selectedRow, _selectedColumn, value))
        {
            txtStatus.Text = "同行、同列或同宫不能重复";
            return;
        }

        _values[_selectedRow, _selectedColumn] = value;
        txtStatus.Text = "点击空格，再选数字";
        RefreshBoard();
    }

    private bool HasDuplicate(int row, int column, int value)
    {
        for (int index = 0; index < BoardSize; index++)
        {
            if ((index != column && _values[row, index] == value) ||
                (index != row && _values[index, column] == value))
            {
                return true;
            }
        }

        int boxRow = row / 3 * 3;
        int boxColumn = column / 3 * 3;
        for (int boxR = boxRow; boxR < boxRow + 3; boxR++)
        {
            for (int boxC = boxColumn; boxC < boxColumn + 3; boxC++)
            {
                if ((boxR != row || boxC != column) && _values[boxR, boxC] == value)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        bool hasEmptyCell = false;
        bool hasDuplicate = false;
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                if (_values[row, column] == 0)
                {
                    hasEmptyCell = true;
                    continue;
                }

                hasDuplicate |= HasDuplicate(row, column, _values[row, column]);
            }
        }

        RefreshBoard();
        if (hasDuplicate)
        {
            txtStatus.Text = "有同行、同列或同宫重复数字";
        }
        else if (IsSolved())
        {
            txtStatus.Text = "完成啦！";
        }
        else if (!hasEmptyCell)
        {
            txtStatus.Text = "规则正确，但答案还不正确";
            MarkIncorrectEntries();
        }
        else
        {
            txtStatus.Text = "还有空格，继续加油";
        }
    }

    private bool IsSolved()
    {
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                if (_values[row, column] != _solution[row, column])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void MarkIncorrectEntries()
    {
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                if (!_fixedCells[row, column] && _values[row, column] != 0 &&
                    _values[row, column] != _solution[row, column])
                {
                    _cellTexts[row, column].Foreground = new SolidColorBrush(Color.FromRgb(0xD2, 0x4E, 0x42));
                }
            }
        }
    }

    private void NewGameButton_Click(object sender, RoutedEventArgs e)
    {
        StartNewGame();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Q)
        {
            Close();
            e.Handled = true;
            return;
        }

        int digit = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => 0
        };

        if (digit != 0)
        {
            SetSelectedValue(digit);
            e.Handled = true;
        }
        else if (e.Key is Key.Back or Key.Delete)
        {
            SetSelectedValue(0);
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RefreshBoard()
    {
        for (int row = 0; row < BoardSize; row++)
        {
            for (int column = 0; column < BoardSize; column++)
            {
                _cellTexts[row, column].Text = _values[row, column] == 0 ? string.Empty : _values[row, column].ToString();
                _cellTexts[row, column].FontWeight = _fixedCells[row, column] ? FontWeights.SemiBold : FontWeights.Normal;
                _cellTexts[row, column].Foreground = _fixedCells[row, column]
                    ? new SolidColorBrush(Color.FromRgb(0xE1, 0xE6, 0xEF))
                    : new SolidColorBrush(Color.FromRgb(0x7E, 0xD6, 0xC5));

                bool selected = row == _selectedRow && column == _selectedColumn;
                bool related = _selectedRow >= 0 &&
                    (row == _selectedRow || column == _selectedColumn ||
                     row / 3 == _selectedRow / 3 && column / 3 == _selectedColumn / 3);
                _cellBorders[row, column].Background = selected
                    ? new SolidColorBrush(Color.FromRgb(0x54, 0x47, 0x32))
                    : related
                        ? new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x48))
                        : GetCellBackground(row, column);
            }
        }
    }
}
