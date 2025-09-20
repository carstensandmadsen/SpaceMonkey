using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Numerics;
using System.IO;
using System.IO.MemoryMappedFiles;
using NoiseFilters;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using CMCustomUDP;

namespace GenericTelemetryProvider
{
    class WreckfestTelemetryProvider : GenericProviderBase
    {
        Int64 memoryAddress;
        Int64 memoryAddressGear;
        public static byte[] gear = new byte[1] {4};
        Thread t;
        Process mainProcess = null;

        public string vehicleString;

        public WreckfestUI ui;


        public override void Run()
        {
            base.Run();


            Process[] processes = Process.GetProcesses();

            foreach (Process process in processes)
            {
                if (process.ProcessName.Contains("Wreckfest"))
                    mainProcess = process;
            }

            if (mainProcess == null) //no processes, better stop
            {

                ui.StatusTextChanged("Wreckfest_x64.exe exe not running!");
                return;
            }


            //For current WF builds we can start at //1400000000 safely. 
            long lStart = 1400000000;
            lStart -= 1000000;//skip a meg back
            if (lStart < 0) lStart = 0;

            RegularMemoryScan scan = new RegularMemoryScan(mainProcess, lStart, 140737488355327); //32gig            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scan.ScanCompleted += new RegularMemoryScan.ScanCompletedEventHandler(scan_ScanCompleted);
            scan.ScanCanceled += new RegularMemoryScan.ScanCanceledEventHandler(scan_ScanCanceled);
            string scanString = "carRootNode" + vehicleString;
            scan.StartScanForString(scanString, 1);


            RegularMemoryScan scanGear = new RegularMemoryScan(mainProcess, lStart, 140737488355327); //32gig            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scanGear.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scanGear.ScanCompleted += new RegularMemoryScan.ScanCompletedEventHandler(scan_ScanCompletedGear);
            scanGear.ScanCanceled += new RegularMemoryScan.ScanCanceledEventHandler(scan_ScanCanceled);
            byte[] scanBytes = new byte[] {
                0xE1, 0xFA, 0x02, 0x44,
                0x00, 0x00, 0x00, 0x3E,
                0x00, 0x00, 0x00, 0x00
            };
            scanGear.StartScanForByteArray(scanBytes, 1);
            

        }

        void ScanComplete()
        {
            ProcessMemoryReader reader = new ProcessMemoryReader();
            reader.ReadProcess = mainProcess;
            UInt64 readSize = 4 * 4 * 4;
            byte[] readBuffer = new byte[readSize];
            byte[] lastReadBuffer = new byte[readSize];
            reader.OpenProcess();
            
            ProcessMemoryReader readerGear = new ProcessMemoryReader();
            readerGear.ReadProcess = mainProcess;
            UInt64 readSizeGear = 1;
            byte[] readBufferGear = new byte[readSizeGear];
            byte[] lastReadBufferGear = new byte[readSizeGear];
            readerGear.OpenProcess();
            

            float frameRateSecs = 1.0f / 60.0f;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            StartSending();

            while (!IsStopped)
            {
                try
                {
                    Matrix4x4 transform = Matrix4x4.Identity;
                    Int64 byteReadSize;
                    Int64 byteReadSizeGear;
                    bool different = false;
                    do
                    {
                        //read
                        reader.ReadProcessMemory((IntPtr)memoryAddress, readSize, out byteReadSize, readBuffer);

                        if (byteReadSize == 0)
                        {
                            continue;
                        }

                        //check if different
                        for (int i = 0; i < (int)readSize; ++i)
                        {
                            if (readBuffer[i] != lastReadBuffer[i])
                            {
                                different = true;
                                break;
                            }
                        }

                        //sleep until the end of the frame
                        if (different)
                            Thread.Sleep(1);


                    } while (!different);


                    //read transform
                    reader.ReadProcessMemory((IntPtr)memoryAddress, readSize, out byteReadSize, readBuffer);
                    readerGear.ReadProcessMemory((IntPtr)memoryAddressGear, readSizeGear, out byteReadSizeGear, readBufferGear);

                    if (byteReadSize == 0)
                    {
                        Console.WriteLine("REEAAALLY DONT WANT THIS TO HAPPEN");
                        continue;
                    }

                    Buffer.BlockCopy(readBuffer, 0, lastReadBuffer, 0, readBuffer.Length);
                    Buffer.BlockCopy(readBufferGear, 0, lastReadBufferGear, 0, readBufferGear.Length);

                    float[] floats = new float[4 * 4];

                    Buffer.BlockCopy(readBuffer, 0, floats, 0, readBuffer.Length);
                    Buffer.BlockCopy(readBufferGear, 0, WreckfestTelemetryProvider.gear, 0, readBufferGear.Length);

                    Matrix4x4 newTransform = new Matrix4x4(floats[0], floats[1], floats[2], floats[3]
                                , floats[4], floats[5], floats[6], floats[7]
                                , floats[8], floats[9], floats[10], floats[11]
                                , floats[12], floats[13], floats[14], floats[15]);

                    ProcessTransform(newTransform, frameRateSecs);

                }
                catch (Exception e)
                {
                    Thread.Sleep(1000);
                }

            }
            reader.CloseHandle();
            readerGear.CloseHandle();

            StopSending();

            Thread.CurrentThread.Join();

        }

        public override bool ProcessTransform(Matrix4x4 newTransform, float inDT)
        {
            if (!base.ProcessTransform(newTransform, inDT))
                return false;

            ui.DebugTextChanged(JsonConvert.SerializeObject(filteredData, Formatting.Indented) + "\n dt: " + dt + "\n steer: " + InputModule.Instance.controller.leftThumb.X + "\n accel: " + InputModule.Instance.controller.rightTrigger + "\n brake: " + InputModule.Instance.controller.leftTrigger + ", " + "\n rht: " + rht.X + ", " + rht.Y + ", " + rht.Z + "\n up: " + up.X + ", " + up.Y + ", " + up.Z + "\n fwd: " + fwd.X + ", " + fwd.Y + ", " + fwd.Z);

            SendFilteredData();

            return true;
        }

        public override void SimulateEngine()
        {
            base.SimulateEngine();
            rawData.gear = (float)(sbyte)WreckfestTelemetryProvider.gear[0]; //2.0f;//gear[1];
            rawData.max_gears = 3.0f;
        }

        void scan_ScanProgressChanged(object sender, ScanProgressChangedEventArgs e)
        {
            ui.ProgressBarChanged(e.Progress);
        }

        void scan_ScanCanceled(object sender, ScanCanceledEventArgs e)
        {
            ui.InitButtonStatusChanged(true);
        }

        void scan_ScanCompletedGear(object sender, ScanCompletedEventArgs e)
        {
            ui.InitButtonStatusChanged(true);

            if (e.MemoryAddresses == null || e.MemoryAddresses.Length == 0)
            {
                ui.StatusTextChanged("Failed2!");

                return;
            }
            Utils.DebugLog("Found gearPattern at: " + e.MemoryAddresses[0].ToString("X"));
            Utils.DebugLog("Found gear1 at: " + (e.MemoryAddresses[0] + 0x1C).ToString("X"));
            
            Utils.DebugLog("Found gear2 at: " + (e.MemoryAddresses[0] + 28).ToString("X"));

            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 4); //offset backwards from found address to start of matrix
            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 8); //offset backwards from found address to start of matrix
            memoryAddressGear = e.MemoryAddresses[0] + 28;// (((4 * 4 * 4) * 2) + 8); //offset backwards from found address to start of matrix

            ui.StatusTextChanged("Success2");

            ProcessMemoryReader readerGear = new ProcessMemoryReader();
            readerGear.ReadProcess = mainProcess;
            UInt64 readSizeGear = 1;            
            Int64 byteReadSizeGear;
            byte[] readBufferGear = new byte[readSizeGear];
            byte[] lastReadBufferGear = new byte[readSizeGear];
            readerGear.OpenProcess();
            readerGear.ReadProcessMemory((IntPtr)memoryAddressGear, readSizeGear, out byteReadSizeGear, readBufferGear);
            Buffer.BlockCopy(readBufferGear, 0, lastReadBufferGear, 0, readBufferGear.Length);
            byte[] mygear = new byte[] {5};
            Buffer.BlockCopy(readBufferGear, 0, mygear, 0, readBufferGear.Length);
            Utils.DebugLog("Gear value: " + ((sbyte)mygear[0]).ToString("X"));
            readerGear.CloseHandle();

            t = new Thread(ScanComplete);
            t.IsBackground = true;
            t.Start();
        }

        void scan_ScanCompleted(object sender, ScanCompletedEventArgs e)
        {
            ui.InitButtonStatusChanged(true);

            if (e.MemoryAddresses == null || e.MemoryAddresses.Length == 0)
            {
                ui.StatusTextChanged("Failed1!");

                return;
            }

            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 4); //offset backwards from found address to start of matrix
            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 8); //offset backwards from found address to start of matrix
            memoryAddress = e.MemoryAddresses[0] - (((4 * 4 * 4) * 2) + 8); //offset backwards from found address to start of matrix

            ui.StatusTextChanged("Success1");
/*
            t = new Thread(ScanComplete);
            t.IsBackground = true;
            t.Start();
            */
        }

        //public override void CalcAngles()
        //{
        //    base.CalcAngles();

        //    rawData.roll = -(float)rawData.roll;
        //    rawData.yaw = -(float)rawData.yaw;
        //}


        //public override void CalcVelocity()
        //{
        //    base.CalcVelocity();

        //    rawData.local_velocity_x = -(float)rawData.local_velocity_x;
        //}

        public override void StopAllThreads()
        {
            base.StopAllThreads();

            if (t != null)
                t.Join();

        }


    }

}
