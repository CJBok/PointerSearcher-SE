﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Text.RegularExpressions;

namespace PointerSearcher
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            int maxDepth = 4;
            int maxOffsetNum = 1;
            long maxOffsetAddress = 0x800;
            textBoxDepth.Text = maxDepth.ToString();
            textBoxOffsetNum.Text = maxOffsetNum.ToString();
            textBoxOffsetAddress.Text = maxOffsetAddress.ToString("X");
            buttonSearch.Enabled = false;
            buttonNarrowDown.Enabled = false;
            buttonCancel.Enabled = false;
            progressBar1.Maximum = 100;

            result = new List<List<IReverseOrderPath>>();
        }
        Socket s;
        private PointerInfo info;
        private int maxDepth;
        private int targetselect = 0;
        private int maxOffsetNum;
        private long maxOffsetAddress;
        private List<List<IReverseOrderPath>> result;
        private CancellationTokenSource cancel = null;
        private double progressTotal;

        public BinaryReader FileStream { get; private set; }

        private async void buttonRead_Click(object sender, EventArgs e)
        {
            SetProgressBar(0);
            try
            {
                buttonRead.Enabled = false;


                IDumpDataReader reader = CreateDumpDataReader(dataGridView1.Rows[0], false);
                if (reader == null)
                {
                    throw new Exception("Invalid input" + Environment.NewLine + "Check highlighted cell");
                }
                reader.readsetup();
                dataGridView1.Rows[0].Cells[1].Value = "0x" + Convert.ToString(reader.mainStartAddress(), 16);
                dataGridView1.Rows[0].Cells[2].Value = "0x" + Convert.ToString(reader.mainEndAddress(), 16);
                dataGridView1.Rows[0].Cells[2].Value = "0x" + Convert.ToString(reader.mainEndAddress(), 16);
                dataGridView1.Rows[0].Cells[3].Value = "0x" + Convert.ToString(reader.heapStartAddress(), 16);
                dataGridView1.Rows[0].Cells[4].Value = "0x" + Convert.ToString(reader.heapEndAddress(), 16);
                //              dataGridView1.Rows[0].Cells[5].Value = "0x" + Convert.ToString(reader.TargetAddress(), 16);
                buttonSearch.Enabled = false;
                buttonNarrowDown.Enabled = false;
                buttonCancel.Enabled = true;

                cancel = new CancellationTokenSource();
                Progress<int> prog = new Progress<int>(SetProgressBar);

                info = await Task.Run(() => reader.Read(cancel.Token, prog));

                SetProgressBar(100);
                System.Media.SystemSounds.Asterisk.Play();

                buttonSearch.Enabled = true;
            }
            catch (System.OperationCanceledException)
            {
                SetProgressBar(0);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                SetProgressBar(0);
                MessageBox.Show("Read Failed" + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cancel != null)
                {
                    cancel.Dispose();
                }

                buttonCancel.Enabled = false;
                buttonRead.Enabled = true;
            }
        }
        private async void buttonSearch_Click(object sender, EventArgs e)
        {
            result.Clear();
            textBox1.Text = "";
            buttonRead.Enabled = false;
            buttonSearch.Enabled = false;
            buttonNarrowDown.Enabled = true;
            textBox2.Text = dataGridView1.Rows[0].Cells[0].Value.ToString();
            textBox2.Text = textBox2.Text.Remove(textBox2.Text.Length - 4, 4) + "bmk";
            SetProgressBar(0);
            try
            {
                maxDepth = Convert.ToInt32(textBoxDepth.Text);
                maxOffsetNum = Convert.ToInt32(textBoxOffsetNum.Text);
                maxOffsetAddress = Convert.ToInt32(textBoxOffsetAddress.Text, 16);
                long heapStart = Convert.ToInt64(dataGridView1.Rows[0].Cells[3].Value.ToString(), 16);
                long targetAddress = Convert.ToInt64(dataGridView1.Rows[0].Cells[5 + targetselect].Value.ToString(), 16);
                Address address = new Address(MemoryType.HEAP, targetAddress - heapStart);

                if (maxOffsetNum <= 0)
                {
                    MessageBox.Show("Offset Num must be greater than 0", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (maxOffsetAddress < 0)
                {
                    MessageBox.Show("Offset Range must be greater or equal to 0", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    buttonCancel.Enabled = true;

                    cancel = new CancellationTokenSource();
                    Progress<double> prog = new Progress<double>(AddProgressBar);

                    FindPath find = new FindPath(maxOffsetNum, maxOffsetAddress);

                    await Task.Run(() =>
                    {
                        find.Search(cancel.Token, prog, 100.0, info, maxDepth, new List<IReverseOrderPath>(), address, result);
                    });

                    SetProgressBar(100);
                    PrintPath();
                    System.Media.SystemSounds.Asterisk.Play();

                    buttonNarrowDown.Enabled = true;
                }
            }
            catch (System.OperationCanceledException)
            {
                SetProgressBar(0);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                SetProgressBar(0);
                MessageBox.Show("Read Failed" + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonCancel.Enabled = false;
                cancel.Dispose();
            }

            buttonRead.Enabled = true;
            buttonSearch.Enabled = true;
        }
        private void PrintPath()
        {
            textBox1.Text = "";
            if (result.Count > 1000)
            {
                string str;
                str = result.Count.ToString();
                textBox1.Text = str + " results" + Environment.NewLine;
            }
            else if (result.Count > 0)
            {
                foreach (List<IReverseOrderPath> path in result)
                {
                    String str = "main";
                    for (int i = path.Count - 1; i >= 0; i--)
                    {
                        str = path[i].ToString(str);
                    }
                    textBox1.Text += str + Environment.NewLine;
                }
            }
            else
            {
                textBox1.Text = "not found";
            }
        }

        private void ExportPath()
        {

            if (result.Count > 0)
            {
                textBox1.Text = "Exporting result to file ... " + result.Count.ToString();
                String filepath = textBox2.Text;
                if ((filepath == "") || System.IO.File.Exists(filepath))
                {
                    textBox1.Text = "Book Mark File exist";
                    return;
                }
                BinaryWriter BM;
                try
                {
                    BM = new BinaryWriter(new FileStream(filepath, FileMode.Create, FileAccess.Write));
                    BM.BaseStream.Seek(0, SeekOrigin.Begin);
                    int magic = 0x4E5A4445;
                    BM.Write(magic);
                    long fileindex = 0;
                    long depth = 0;
                    long[] chain = new long[13];

                    foreach (List<IReverseOrderPath> path in result)
                    {

                        BM.BaseStream.Seek(134 + fileindex * 8 * 14, SeekOrigin.Begin); // sizeof(pointer_chain_t)  Edizon header size = 134
                        depth = 0;
                        for (int i = path.Count - 1; i >= 0; i--)
                        {
                            if (path[i] is ReverseOrderPathOffset)
                            {
                                chain[depth] = (path[i] as ReverseOrderPathOffset).getOffset();
                            }
                            else
                            {
                                depth++;
                                chain[depth] = 0;
                            }
                        }
                        BM.Write(depth);
                        for (long z = depth; z >= 0; z--)
                            BM.Write(chain[z]);
                        fileindex++;
                    };
                    for (long z = depth + 1; z < 13; z++)
                        BM.Write(chain[z]);
                    BM.BaseStream.Seek(5, SeekOrigin.Begin);
                    BM.Write(result.Count * 8 * 14);
                    BM.BaseStream.Close();
                }
                catch (IOException) { textBox1.Text = "Cannot create file"; }
            }
            else
            {
                textBox1.Text = "not found";
            }
        }

        private void dataGridView1_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.ColumnIndex == 0)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.InitialDirectory = @"";
                ofd.Filter = "EdizonSE DumpFile(*.dmp*)|*.dmp*|All Files(*.*)|*.*";
                ofd.FilterIndex = 1;
                ofd.Title = "select EdiZon SE dump file";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    dataGridView1.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = ofd.FileName;
                }
                IDumpDataReader reader = CreateDumpDataReader(dataGridView1.Rows[e.RowIndex], true);
                if (reader != null)
                {
                    reader.readsetup();
                    dataGridView1.Rows[e.RowIndex].Cells[1].Value = "0x" + Convert.ToString(reader.mainStartAddress(), 16);
                    dataGridView1.Rows[e.RowIndex].Cells[2].Value = "0x" + Convert.ToString(reader.mainEndAddress(), 16);
                    dataGridView1.Rows[e.RowIndex].Cells[3].Value = "0x" + Convert.ToString(reader.heapStartAddress(), 16);
                    dataGridView1.Rows[e.RowIndex].Cells[4].Value = "0x" + Convert.ToString(reader.heapEndAddress(), 16);
                    dataGridView1.Rows[e.RowIndex].Cells[5].Value = "0x" + Convert.ToString(reader.TargetAddress(), 16);
                    // BM1

                }
            }
        }

        private async void buttonNarrowDown_Click(object sender, EventArgs e)
        {

            try
            {
                SetProgressBar(0);
                Dictionary<IDumpDataReader, long> dumps = new Dictionary<IDumpDataReader, long>();
                for (int i = 1; i < dataGridView1.Rows.Count; i++)
                {
                    DataGridViewRow row = dataGridView1.Rows[i];
                    ClearRowBackColor(row);
                    if (IsBlankRow(row))
                    {
                        continue;
                    }
                    IDumpDataReader reader = CreateDumpDataReader(row, true);
                    if (reader != null)
                    {
                        reader.readsetup();
                        dataGridView1.Rows[i].Cells[1].Value = "0x" + Convert.ToString(reader.mainStartAddress(), 16);
                        dataGridView1.Rows[i].Cells[2].Value = "0x" + Convert.ToString(reader.mainEndAddress(), 16);
                        dataGridView1.Rows[i].Cells[3].Value = "0x" + Convert.ToString(reader.heapStartAddress(), 16);
                        dataGridView1.Rows[i].Cells[4].Value = "0x" + Convert.ToString(reader.heapEndAddress(), 16);
                        //                     dataGridView1.Rows[i].Cells[5].Value = "0x" + Convert.ToString(reader.TargetAddress(), 16);
                        long target = Convert.ToInt64(row.Cells[5 + targetselect].Value.ToString(), 16);

                        dumps.Add(reader, target);
                    }
                }
                if (dumps.Count == 0)
                {
                    throw new Exception("Fill out 2nd line to narrow down");
                }
                buttonRead.Enabled = false;
                buttonSearch.Enabled = false;
                buttonNarrowDown.Enabled = false;
                buttonCancel.Enabled = true;

                cancel = new CancellationTokenSource();
                Progress<int> prog = new Progress<int>(SetProgressBar);

                List<List<IReverseOrderPath>> copyList = new List<List<IReverseOrderPath>>(result);

                result = await Task.Run(() => FindPath.NarrowDown(cancel.Token, prog, result, dumps));

                SetProgressBar(100);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (System.OperationCanceledException)
            {
                SetProgressBar(0);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                SetProgressBar(0);
                MessageBox.Show(Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cancel != null)
                {
                    cancel.Dispose();
                }
                PrintPath();

                buttonRead.Enabled = true;
                buttonSearch.Enabled = true;
                buttonNarrowDown.Enabled = true;
                buttonCancel.Enabled = false;
            }
        }
        private bool IsBlankRow(DataGridViewRow row)
        {
            for (int i = 0; i <= 5; i++)
            {
                if (row.Cells[i].Value == null)
                {
                    continue;
                }
                if (row.Cells[i].Value.ToString() != "")
                {
                    return false;
                }
            }
            return true;
        }
        private void ClearRowBackColor(DataGridViewRow row)
        {
            for (int i = 0; i <= 5; i++)
            {
                row.Cells[i].Style.BackColor = Color.White;
            }
        }
        private IDumpDataReader CreateDumpDataReader(DataGridViewRow row, bool allowUnknownTarget)
        {
            bool canCreate = true;
            String path = "";
            long mainStart = -1;
            long mainEnd = -1;
            long heapStart = -1;
            long heapEnd = -1;

            if (row.Cells[0].Value != null)
            {
                path = row.Cells[0].Value.ToString();
            }
            if ((path == "") || !System.IO.File.Exists(path))
            {
                row.Cells[0].Style.BackColor = Color.Red;
                canCreate = false;
            }
            if (!canCreate)
            {
                return null;
            }
            return new NoexsDumpDataReader(path, mainStart, mainEnd, heapStart, heapEnd);
        }

        private void dataGridView1_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            dataGridView1.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor = Color.White;
            //  dataGridView1.BeginEdit(true);
        }
        private void SetProgressBar(int percent)
        {
            progressBar1.Value = percent;
            progressTotal = percent;
        }
        private void AddProgressBar(double percent)
        {
            progressTotal += percent;
            if (progressTotal > 100)
            {
                progressTotal = 100;
            }
            progressBar1.Value = (int)progressTotal;
        }

        private void buttonCancel_Click_1(object sender, EventArgs e)
        {
            if (cancel != null)
            {
                cancel.Cancel();
            }
        }

        private void textBoxDepth_TextChanged(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            dataGridView1.Rows.Add();
            dataGridView1.Rows.Add();
            dataGridView1.Rows.Add();
            dataGridView1.Rows.Add();
            ipBox.Text = ConfigurationManager.AppSettings["ipAddress"];
        }

        private void Export_to_SE_Click(object sender, EventArgs e)
        {
            ExportPath();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            PrintPath();
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            targetselect = 0;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            targetselect = 1;
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            targetselect = 2;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            string ipPattern = @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";
            if (!Regex.IsMatch(ipBox.Text, ipPattern))
            {
                ipBox.BackColor = System.Drawing.Color.Red;
                return;
            }
            s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ipBox.Text), 7331);
            Configuration config = ConfigurationManager.OpenExeConfiguration(Application.ExecutablePath);
            if (config.AppSettings.Settings["ipAddress"] == null) config.AppSettings.Settings.Add("ipAddress", ipBox.Text);
            else
                config.AppSettings.Settings["ipAddress"].Value = ipBox.Text;
            config.Save(ConfigurationSaveMode.Minimal);
            if (s.Connected == false)
            {
                new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    IAsyncResult result = s.BeginConnect(ep, null, null);
                    bool conSuceded = result.AsyncWaitHandle.WaitOne(3000, true);
                    if (conSuceded == true)
                    {
                        try
                        {
                            s.EndConnect(result);
                        }
                        catch
                        {
                            this.ipBox.Invoke((MethodInvoker)delegate
                            {
                                ipBox.BackColor = System.Drawing.Color.Red;
                                ipBox.ReadOnly = false;
                            });
                            return;
                        }

                        this.connectBtn.Invoke((MethodInvoker)delegate
                        {
                            this.connectBtn.Enabled = false;
                        });
                        this.ipBox.Invoke((MethodInvoker)delegate
                        {
                            ipBox.BackColor = System.Drawing.Color.Green;
                            ipBox.ReadOnly = true;
                            //this.refreshBtn.Visible = true;
                            //this.Player1Btn.Visible = true;
                            //this.Player2Btn.Visible = true;
                        });

                    }
                    else
                    {
                        s.Close();
                        this.ipBox.Invoke((MethodInvoker)delegate
                        {
                            ipBox.BackColor = System.Drawing.Color.Red;
                        });
                        MessageBox.Show("Could not connect to the SE tools server, Go to https://github.com/ for help.");
                    }
                }).Start();
            }
        }

        private void getstatus_Click(object sender, EventArgs e)
        {
            byte[] msg = { 0x10 };
            int a = s.Send(msg);
            byte[] k = new byte[4];
            int c = s.Receive(k);
            int count = BitConverter.ToInt32(k, 0);
            byte[] b = new byte[count * 8];
            int d = s.Receive(b);
            long pid = BitConverter.ToInt64(b, (count-2) *8);
            pidBox.Text = "0x" + Convert.ToString(pid);
            int f = s.Available;
            c = s.Receive(k);

            msg[0] = 0x01;
            a = s.Send(msg);
            k = new byte[4];
            while (s.Available < 4) ;
            c = s.Receive(k);
            count = BitConverter.ToInt32(k, 0);
            statusBox.Text = "0x" + Convert.ToString(count,16);
            f = s.Available;
            b = new byte[f];
            s.Receive(b);

            msg[0] = 0x0E; //_current_pid
            a = s.Send(msg);
            k = new byte[8];
            while (s.Available < 8) ;
            c = s.Receive(k);
            long curpid = BitConverter.ToInt64(k, 0);
            curpidBox.Text = "0x" + Convert.ToString(curpid, 16);
            while (s.Available < 4) ;
            b = new byte[s.Available];
            s.Receive(b);

            msg[0] = 0x0A; //_attach
            a = s.Send(msg);
            k = BitConverter.GetBytes(curpid);
            a = s.Send(k);
            while (s.Available < 4) ;
            b = new byte[s.Available];
            s.Receive(b);

            msg[0] = 0x1A; //_dump_ptr
            a = s.Send(msg);
            k = new byte[8 * 4];
            while (s.Available < 8 * 4) ;
            c = s.Receive(k);
            long address1 = BitConverter.ToInt64(k, 0);
            long address2 = BitConverter.ToInt64(k, 8);
            long address3 = BitConverter.ToInt64(k, 16);
            long address4 = BitConverter.ToInt64(k, 24);
            //curpidBox.Text = "0x" + Convert.ToString(count, 16);
            while (s.Available < 4) ;
            b = new byte[s.Available];
            s.Receive(b);
        }


    }
}
