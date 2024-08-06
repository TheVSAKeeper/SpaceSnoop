﻿namespace SpaceSnoop
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _hardDiskComboBox = new ComboBox();
            _startButton = new Button();
            _stopButton = new Button();
            _directoriesTreeView = new TreeView();
            _useMultithreadingCheckBox = new CheckBox();
            _uiLogsTextBox = new RichTextBox();
            _calculateProgressBar = new ProgressBar();
            _sortGroupBox = new GroupBox();
            _invertSortCheckBox = new CheckBox();
            _sortModeComboBox = new ComboBox();
            _controlGroupBox = new GroupBox();
            _sortGroupBox.SuspendLayout();
            _controlGroupBox.SuspendLayout();
            SuspendLayout();
            // 
            // _hardDiskComboBox
            // 
            _hardDiskComboBox.FormattingEnabled = true;
            _hardDiskComboBox.Location = new Point(6, 22);
            _hardDiskComboBox.Name = "_hardDiskComboBox";
            _hardDiskComboBox.Size = new Size(210, 23);
            _hardDiskComboBox.TabIndex = 0;
            // 
            // _startButton
            // 
            _startButton.Location = new Point(109, 51);
            _startButton.Name = "_startButton";
            _startButton.Size = new Size(107, 23);
            _startButton.TabIndex = 1;
            _startButton.Text = "start";
            _startButton.UseVisualStyleBackColor = true;
            _startButton.Click += OnStartButtonClicked;
            // 
            // _stopButton
            // 
            _stopButton.Location = new Point(6, 103);
            _stopButton.Name = "_stopButton";
            _stopButton.Size = new Size(210, 23);
            _stopButton.TabIndex = 2;
            _stopButton.Text = "stop";
            _stopButton.UseVisualStyleBackColor = true;
            _stopButton.Click += OnStopButtonClicked;
            // 
            // _directoriesTreeView
            // 
            _directoriesTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _directoriesTreeView.Location = new Point(240, 12);
            _directoriesTreeView.Name = "_directoriesTreeView";
            _directoriesTreeView.Size = new Size(577, 533);
            _directoriesTreeView.TabIndex = 3;
            _directoriesTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
            _directoriesTreeView.NodeMouseClick += OnNodeMouseClicked;
            // 
            // _useMultithreadingCheckBox
            // 
            _useMultithreadingCheckBox.AutoSize = true;
            _useMultithreadingCheckBox.Location = new Point(6, 55);
            _useMultithreadingCheckBox.Name = "_useMultithreadingCheckBox";
            _useMultithreadingCheckBox.Size = new Size(90, 19);
            _useMultithreadingCheckBox.TabIndex = 5;
            _useMultithreadingCheckBox.Text = "MultiThread";
            _useMultithreadingCheckBox.UseVisualStyleBackColor = true;
            // 
            // _uiLogsTextBox
            // 
            _uiLogsTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _uiLogsTextBox.Location = new Point(12, 209);
            _uiLogsTextBox.Name = "_uiLogsTextBox";
            _uiLogsTextBox.ReadOnly = true;
            _uiLogsTextBox.Size = new Size(222, 336);
            _uiLogsTextBox.TabIndex = 6;
            _uiLogsTextBox.Text = "";
            // 
            // _calculateProgressBar
            // 
            _calculateProgressBar.Location = new Point(6, 80);
            _calculateProgressBar.Name = "_calculateProgressBar";
            _calculateProgressBar.Size = new Size(210, 17);
            _calculateProgressBar.TabIndex = 7;
            // 
            // _sortGroupBox
            // 
            _sortGroupBox.Controls.Add(_invertSortCheckBox);
            _sortGroupBox.Controls.Add(_sortModeComboBox);
            _sortGroupBox.Location = new Point(12, 152);
            _sortGroupBox.Name = "_sortGroupBox";
            _sortGroupBox.Size = new Size(222, 51);
            _sortGroupBox.TabIndex = 8;
            _sortGroupBox.TabStop = false;
            _sortGroupBox.Text = "Сортировка";
            // 
            // _invertSortCheckBox
            // 
            _invertSortCheckBox.AutoSize = true;
            _invertSortCheckBox.Location = new Point(160, 24);
            _invertSortCheckBox.Name = "_invertSortCheckBox";
            _invertSortCheckBox.Size = new Size(56, 19);
            _invertSortCheckBox.TabIndex = 9;
            _invertSortCheckBox.Text = "Invert";
            _invertSortCheckBox.UseVisualStyleBackColor = true;
            _invertSortCheckBox.CheckedChanged += OnInvertSortCheckBoxChanged;
            // 
            // _sortModeComboBox
            // 
            _sortModeComboBox.FormattingEnabled = true;
            _sortModeComboBox.Location = new Point(6, 22);
            _sortModeComboBox.Name = "_sortModeComboBox";
            _sortModeComboBox.Size = new Size(138, 23);
            _sortModeComboBox.TabIndex = 0;
            // 
            // _controlGroupBox
            // 
            _controlGroupBox.Controls.Add(_hardDiskComboBox);
            _controlGroupBox.Controls.Add(_startButton);
            _controlGroupBox.Controls.Add(_calculateProgressBar);
            _controlGroupBox.Controls.Add(_useMultithreadingCheckBox);
            _controlGroupBox.Controls.Add(_stopButton);
            _controlGroupBox.Location = new Point(12, 12);
            _controlGroupBox.Name = "_controlGroupBox";
            _controlGroupBox.Size = new Size(222, 134);
            _controlGroupBox.TabIndex = 9;
            _controlGroupBox.TabStop = false;
            _controlGroupBox.Text = "Сканирование";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(829, 557);
            Controls.Add(_controlGroupBox);
            Controls.Add(_sortGroupBox);
            Controls.Add(_uiLogsTextBox);
            Controls.Add(_directoriesTreeView);
            Name = "MainForm";
            Text = "SpaceSnoop";
            Load += OnFormLoaded;
            _sortGroupBox.ResumeLayout(false);
            _sortGroupBox.PerformLayout();
            _controlGroupBox.ResumeLayout(false);
            _controlGroupBox.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private ComboBox _hardDiskComboBox;
        private Button _startButton;
        private Button _stopButton;
        private TreeView _directoriesTreeView;
        private CheckBox _useMultithreadingCheckBox;
        private RichTextBox _uiLogsTextBox;
        private ProgressBar _calculateProgressBar;
        private GroupBox _sortGroupBox;
        private ComboBox _sortModeComboBox;
        private CheckBox _invertSortCheckBox;
        private GroupBox _controlGroupBox;
    }
}
