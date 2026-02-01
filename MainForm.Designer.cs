namespace InputBox
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            TLPHost = new TableLayoutPanel();
            PInputHost = new Panel();
            TBInput = new TextBox();
            BtnCopy = new Button();
            TLPHost.SuspendLayout();
            PInputHost.SuspendLayout();
            SuspendLayout();
            // 
            // TLPHost
            // 
            TLPHost.ColumnCount = 2;
            TLPHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            TLPHost.ColumnStyles.Add(new ColumnStyle());
            TLPHost.Controls.Add(PInputHost, 0, 0);
            TLPHost.Controls.Add(BtnCopy, 1, 0);
            TLPHost.Dock = DockStyle.Fill;
            TLPHost.Location = new Point(0, 0);
            TLPHost.Name = "TLPHost";
            TLPHost.RowCount = 1;
            TLPHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            TLPHost.Size = new Size(384, 46);
            TLPHost.TabIndex = 0;
            // 
            // PInputHost
            // 
            PInputHost.Controls.Add(TBInput);
            PInputHost.Dock = DockStyle.Fill;
            PInputHost.Location = new Point(3, 3);
            PInputHost.Name = "PInputHost";
            PInputHost.Padding = new Padding(3);
            PInputHost.Size = new Size(257, 40);
            PInputHost.TabIndex = 0;
            // 
            // TBInput
            // 
            TBInput.AccessibleDescription = "請在此輸入要複製到剪貼簿的文字。";
            TBInput.AccessibleName = "輸入文字";
            TBInput.AccessibleRole = AccessibleRole.Text;
            TBInput.BorderStyle = BorderStyle.None;
            TBInput.Dock = DockStyle.Fill;
            TBInput.Font = new Font("Microsoft JhengHei UI", 18F, FontStyle.Regular, GraphicsUnit.Point, 136);
            TBInput.ImeMode = ImeMode.On;
            TBInput.Location = new Point(3, 3);
            TBInput.Margin = new Padding(4);
            TBInput.Multiline = true;
            TBInput.Name = "TBInput";
            TBInput.PlaceholderText = "請輸入文字……";
            TBInput.Size = new Size(251, 34);
            TBInput.TabIndex = 0;
            TBInput.Enter += TBInput_Enter;
            TBInput.KeyDown += TBInput_KeyDown;
            TBInput.Leave += TBInput_Leave;
            // 
            // BtnCopy
            // 
            BtnCopy.AccessibleDescription = "將文字方塊中的文字複製到剪貼簿。";
            BtnCopy.AccessibleName = "複製到剪貼簿";
            BtnCopy.AccessibleRole = AccessibleRole.PushButton;
            BtnCopy.Dock = DockStyle.Fill;
            BtnCopy.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 136);
            BtnCopy.Location = new Point(267, 4);
            BtnCopy.Margin = new Padding(4);
            BtnCopy.Name = "BtnCopy";
            BtnCopy.Size = new Size(113, 38);
            BtnCopy.TabIndex = 1;
            BtnCopy.Text = "複製到剪貼簿";
            BtnCopy.UseMnemonic = false;
            BtnCopy.UseVisualStyleBackColor = true;
            BtnCopy.Click += BtnCopy_Click;
            // 
            // MainForm
            // 
            AccessibleDescription = "輸入文字後，可按下複製到剪貼簿按鈕以複製文字到剪貼簿。";
            AccessibleName = "文字輸入與複製";
            AccessibleRole = AccessibleRole.Dialog;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(384, 46);
            Controls.Add(TLPHost);
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 136);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "MainForm";
            Text = "輸入框";
            TopMost = true;
            Activated += MainForm_Activated;
            Shown += MainForm_Shown;
            TLPHost.ResumeLayout(false);
            PInputHost.ResumeLayout(false);
            PInputHost.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel TLPHost;
        private Button BtnCopy;
        private TextBox TBInput;
        private Panel PInputHost;
    }
}
