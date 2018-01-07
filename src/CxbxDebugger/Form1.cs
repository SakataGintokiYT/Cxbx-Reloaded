﻿// Written by x1nixmzeng for the Cxbx-Reloaded project
//

using System;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.IO;

namespace CxbxDebugger
{
    public partial class Form1 : Form
    {
        Thread DebuggerWorkerThread;
        Debugger DebuggerInst;
        string[] CachedArgs;

        DebuggerFormEvents DebugEvents;

        List<DebuggerThread> DebugThreads = new List<DebuggerThread>();

        public Form1()
        {
            InitializeComponent();

            string[] args = Environment.GetCommandLineArgs();

            // Arguments are expected before the Form is created
            if (args.Length < 2)
            {
                throw new Exception("Incorrect usage");
            }

            var items = new List<string>(args.Length - 1);
            for (int i = 1; i < args.Length; ++i)
            {
                items.Add(args[i]);
                //listBox1.Items.Add("Arg: " + args[i]);
            }

            CachedArgs = items.ToArray();

            DebugEvents = new DebuggerFormEvents(this);

            SetDebugProcessActive(false);

            // TODO: Wait for user to start this?
            //StartDebugging();
        }

        private void StartDebugging()
        {
            bool Create = false;

            if (DebuggerWorkerThread == null)
            {
                // First launch
                Create = true;
            }
            else if (DebuggerWorkerThread.ThreadState == System.Threading.ThreadState.Stopped)
            {
                // Further launches
                Create = true;
            }

            if (Create)
            {
                // Create debugger instance
                DebuggerInst = new Debugger(CachedArgs);
                DebuggerInst.RegisterEventInterfaces(DebugEvents);

                // Setup new debugger thread
                DebuggerWorkerThread = new Thread(x =>
                {
                    if (DebuggerInst.Launch())
                    {
                        DebuggerInst.RunThreaded();
                    }
                });

                DebuggerWorkerThread.Name = "CxbxDebugger";
                DebuggerWorkerThread.Start();
            }
        }

        private void PopulateThreadList(ComboBox.ObjectCollection Items, DebuggerThread FocusThread)
        {
            Items.Clear();

            foreach (DebuggerThread Thread in DebugThreads)
            {
                bool IsMainThread = (Thread.Handle == Thread.OwningProcess.MainThread.Handle);
                bool IsFocusThread = (FocusThread != null) && (Thread.Handle == FocusThread.Handle);
                string DisplayStr = "";
                string PrefixStr = "";

                // Threads with focus are marked differently
                if (IsFocusThread)
                    PrefixStr = "* ";

                // Main threads always override any existing prefix
                if (IsMainThread)
                    PrefixStr = "> ";
                
                DisplayStr = string.Format("{0}[{1}] 0x{2:X8}", PrefixStr, (uint)Thread.Handle, (uint)Thread.StartAddress);
                
                if( Thread.WasSuspended )
                {
                    DisplayStr += " (suspended)";
                }

                Items.Add(DisplayStr);
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (DebuggerWorkerThread != null)
            {
                if (DebuggerWorkerThread.ThreadState == ThreadState.Running)
                {
                    DebuggerWorkerThread.Abort();
                }
            }

            if (DebuggerInst != null)
            {
                DebuggerInst.Dispose();
            }
        }

        private void DebugLog(string Message)
        {
            string MessageStamped = string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), Message);

            if (InvokeRequired)
            {
                // Ensure we Add items on the right thread
                Invoke(new MethodInvoker(delegate ()
                {
                    lbConsole.Items.Insert(0, MessageStamped);
                }));
            }
            else
            {
                lbConsole.Items.Insert(0, MessageStamped);
            }
        }

        private void SetDebugProcessActive(bool Active)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(delegate ()
                {
                    // Disable when active
                    btnStart.Enabled = !Active;

                    // Enable when active
                    btnSuspend.Enabled = Active;
                    btnResume.Enabled = Active;
                }));
            }
            else
            {
                // Disable when active
                btnStart.Enabled = !Active;

                // Enable when active
                btnSuspend.Enabled = Active;
                btnResume.Enabled = Active;
            }
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            lbConsole.Items.Clear();
        }

        class DebuggerFormEvents : IDebuggerGeneralEvents, IDebuggerProcessEvents, IDebuggerModuleEvents, IDebuggerThreadEvents, IDebuggerOutputEvents, IDebuggerExceptionEvents
        {
            Form1 frm;

            public DebuggerFormEvents(Form1 main)
            {
                frm = main;
            }

            private string PrettyExitCode(uint ExitCode)
            {
                string ExitCodeString;

                switch (ExitCode)
                {
                    case 0:
                        ExitCodeString = "Finished";
                        break;

                    case 0xC000013A:
                        // Actual code is STATUS_CONTROL_C_EXIT, but isn't very friendly
                        ExitCodeString = "Debug session ended";
                        break;

                    default:
                        ExitCodeString = string.Format("{0:X8}", ExitCode);
                        break;
                }

                return ExitCodeString;
            }

            public void OnProcessCreate(DebuggerProcess Process)
            {
                // !
            }

            public void OnProcessExit(DebuggerProcess Process, uint ExitCode)
            {
                int remainingThreads = Process.Threads.Count;

                frm.DebugLog(string.Format("Process exited {0} ({1})", Process.ProcessID, PrettyExitCode(ExitCode)));
                frm.DebugLog(string.Format("{0} thread(s) remain open", remainingThreads));
            }

            public void OnDebugStart()
            {
                frm.SetDebugProcessActive(true);
                frm.DebugLog("Started debugging session");
            }

            public void OnDebugEnd()
            {
                frm.SetDebugProcessActive(false);
                frm.DebugLog("Ended debugging session");
            }

            public void OnThreadCreate(DebuggerThread Thread)
            {
                frm.DebugLog(string.Format("Thread created {0}", Thread.ThreadID));
                frm.DebugThreads.Add(Thread);
            }

            public void OnThreadExit(DebuggerThread Thread, uint ExitCode)
            {
                frm.DebugLog(string.Format("Thread exited {0} ({1})", Thread.ThreadID, PrettyExitCode(ExitCode)));
                frm.DebugThreads.Remove(Thread);
            }

            public void OnModuleLoaded(DebuggerModule Module)
            {
                frm.DebugLog(string.Format("Loaded module \"{0}\"", Module.Path));
            }

            public void OnModuleUnloaded(DebuggerModule Module)
            {
                frm.DebugLog(string.Format("Unloaded module \"{0}\"", Module.Path));
            }

            public void OnDebugOutput(string Message)
            {
                frm.DebugLog(string.Format("OutputDebugString \"{0}\"", Message));
            }

            public void OnAccessViolation(DebuggerThread Thread, IntPtr Address)
            {
                string ProcessName = Path.GetFileName(Thread.OwningProcess.Path);
                
                // TODO Include GetLastError string
                string ExceptionMessage = string.Format("Access violation thrown in {0} 0x{1:X8}", ProcessName, (uint)Address);

                frm.DebugLog(ExceptionMessage);

                // Already suspended at this point, so we can rebuild the callstack list
                frm.PopulateThreadList(frm.cbThreads.Items, Thread);
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            StartDebugging();
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            if (DebuggerInst != null)
            {
                DebuggerInst.Break();

                PopulateThreadList(cbThreads.Items, null);
            }
        }

        private void toolStripButton3_Click(object sender, EventArgs e)
        {
            if (DebuggerInst != null)
            {
                DebuggerInst.Resume();
            }
        }

        private void btnDumpCallstack_Click(object sender, EventArgs e)
        {
            int Index = cbThreads.SelectedIndex;
            if (Index == -1)
                return;

            lbRegisters.Items.Clear();

            var Callstack = DebugThreads[Index].CallstackCache;
            foreach(DebuggerStackFrame StackFrame in Callstack.StackFrames)
            {
                string FrameString = string.Format("{0:X8}", (uint)StackFrame.PC);

                // Try to resolve the symbol name
                var Symbol = DebuggerInst.ResolveSymbol(StackFrame.PC);
                if( Symbol != null)
                {
                    // TODO Investigate why this is a constant offset in the last few frames
                    uint Offset = (uint)StackFrame.PC - Symbol.AddrBegin;

                    FrameString = string.Format("{0} +0x{1:X}", Symbol.Name, Offset);
                }

                lbRegisters.Items.Add(FrameString);
            }
        }
    }
}
