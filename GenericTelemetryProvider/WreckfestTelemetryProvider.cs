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
        Int64 memoryAddressMatrix;
        Int64 memoryAddressEngineData;
        Int64 memoryAddressGear;
        Int64 memoryAddressRpm;
        Int64 memoryAddressClutch;
        int gear = 0;
        int gearMax = 1;
        float rpm = 0.0f;
        float rpmMin = 9999f;
        float rpmMax = 0.0f;
        float clutch = 0.0f;
        Thread t;
        Process mainProcess = null;

        public string vehicleString;

        public WreckfestUI ui;


        public override void Run()
        {
            base.Run();

            //reset values
            gearMax = 1;
            rpmMin = 9999f;
            rpmMax = 0.0f;


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

            //scan for carRootNode
            RegularMemoryScan scan = new RegularMemoryScan(mainProcess, lStart, 140737488355327); //32gig            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scan.ScanCompleted += new RegularMemoryScan.ScanCompletedEventHandler(scan_ScanCompleted); ;
            scan.ScanCanceled += new RegularMemoryScan.ScanCanceledEventHandler(scan_ScanCanceled);
            string scanString = "carRootNode" + vehicleString;
            scan.StartScanForString(scanString, 1);

            //scan for engine data
            RegularMemoryScan scanForEngineData = new RegularMemoryScan(mainProcess, 0x10000000000, 0x40000000000); //32gig            scan.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scanForEngineData.ScanProgressChanged += new RegularMemoryScan.ScanProgressedEventHandler(scan_ScanProgressChanged);
            scanForEngineData.ScanCompleted += new RegularMemoryScan.ScanCompletedEventHandler(scan_ScanCompletedEngineData);
            scanForEngineData.ScanCanceled += new RegularMemoryScan.ScanCanceledEventHandler(scan_ScanCanceled);
            byte[] scanBytes = new byte[] { //"default" followed by 25 zeroes = 64 65 66 61 75 6C 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
                0x64, 0x65, 0x66, 0x61, 0x75, 0x6C, 0x74, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            scanForEngineData.StartScanForByteArray(scanBytes, 1);
        }

        void ScanComplete()
        {
            ProcessMemoryReader reader = new ProcessMemoryReader();
            reader.ReadProcess = mainProcess;
            reader.OpenProcess();

            UInt64 readSizeMatrix = 4 * 4 * 4;
            byte[] readBufferMatrix = new byte[readSizeMatrix];
            byte[] lastReadBufferMatrix = new byte[readSizeMatrix];

            UInt64 readSizeGear = 4;
            byte[] readBufferGear = new byte[readSizeGear];
            byte[] lastReadBufferGear = new byte[readSizeGear];

            UInt64 readSizeRpm = 4;
            byte[] readBufferRpm = new byte[readSizeRpm];
            byte[] lastReadBufferRpm = new byte[readSizeRpm];

            UInt64 readSizeClutch = 4;
            byte[] readBufferClutch = new byte[readSizeClutch];
            byte[] lastReadBufferClutch = new byte[readSizeClutch];

            UInt64 readSizeAll = readSizeMatrix + readSizeGear + readSizeRpm + readSizeClutch;
            //byte[] readBufferAll = new byte[readSizeAll];
            byte[] lastReadBufferAll = new byte[readSizeAll];


            float frameRateSecs = 1.0f / 60.0f;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            StartSending();

            while (!IsStopped)
            {
                try
                {
                    Matrix4x4 transform = Matrix4x4.Identity;
                    Int64 byteReadSizeMatrix;
                    Int64 byteReadSizeGear;
                    Int64 byteReadSizeRpm;
                    Int64 byteReadSizeClutch;
                    Int64 byteReadSizeAll;
                    bool different = false;
                    do
                    {
                        //read
                        reader.ReadProcessMemory((IntPtr)memoryAddressMatrix, readSizeMatrix, out byteReadSizeMatrix, readBufferMatrix);
                        reader.ReadProcessMemory((IntPtr)memoryAddressGear, readSizeGear, out byteReadSizeGear, readBufferGear);
                        reader.ReadProcessMemory((IntPtr)memoryAddressRpm, readSizeRpm, out byteReadSizeRpm, readBufferRpm);
                        reader.ReadProcessMemory((IntPtr)memoryAddressClutch, readSizeClutch, out byteReadSizeClutch, readBufferClutch);

                        byteReadSizeAll = byteReadSizeMatrix + byteReadSizeGear + byteReadSizeRpm + byteReadSizeClutch;
                        if (byteReadSizeAll == 0)
                        {
                            continue;
                        }

                        byte[] readBufferAll = readBufferMatrix.Concat(readBufferGear).Concat(readBufferRpm).Concat(readBufferClutch).ToArray();
                        //check if different
                        for (int i = 0; i < (int)readSizeAll; ++i)
                        {
                            if (readBufferAll[i] != lastReadBufferAll[i])
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
                    // Read current values
                    reader.ReadProcessMemory((IntPtr)memoryAddressMatrix, readSizeMatrix, out byteReadSizeMatrix, readBufferMatrix);
                    reader.ReadProcessMemory((IntPtr)memoryAddressGear, readSizeGear, out byteReadSizeGear, readBufferGear);
                    reader.ReadProcessMemory((IntPtr)memoryAddressRpm, readSizeRpm, out byteReadSizeRpm, readBufferRpm);
                    reader.ReadProcessMemory((IntPtr)memoryAddressClutch, readSizeClutch, out byteReadSizeClutch, readBufferClutch);

                    byteReadSizeAll = byteReadSizeMatrix + byteReadSizeGear + byteReadSizeRpm + byteReadSizeClutch;
                    if (byteReadSizeAll == 0)
                    {
                        Console.WriteLine("REEAAALLY DONT WANT THIS TO HAPPEN");
                        continue;
                    }

                    // Copy current values for next difference check
                    Buffer.BlockCopy(readBufferMatrix, 0, lastReadBufferAll, 0, readBufferMatrix.Length);
                    Buffer.BlockCopy(readBufferGear, 0, lastReadBufferAll, readBufferMatrix.Length, readBufferGear.Length);
                    Buffer.BlockCopy(readBufferRpm, 0, lastReadBufferAll, readBufferMatrix.Length + readBufferGear.Length, readBufferRpm.Length);
                    Buffer.BlockCopy(readBufferClutch, 0, lastReadBufferAll, readBufferMatrix.Length + readBufferGear.Length + readBufferRpm.Length, readBufferClutch.Length);

                    // Get matrix value
                    float[] matrixFloats = new float[4 * 4];
                    Buffer.BlockCopy(readBufferMatrix, 0, matrixFloats, 0, readBufferMatrix.Length);
                    Matrix4x4 newTransform = new Matrix4x4(matrixFloats[0], matrixFloats[1], matrixFloats[2], matrixFloats[3]
                                , matrixFloats[4], matrixFloats[5], matrixFloats[6], matrixFloats[7]
                                , matrixFloats[8], matrixFloats[9], matrixFloats[10], matrixFloats[11]
                                , matrixFloats[12], matrixFloats[13], matrixFloats[14], matrixFloats[15]);

                    // Get gear value
                    int[] gearInts = new int[1];
                    Buffer.BlockCopy(readBufferGear, 0, gearInts, 0, readBufferGear.Length);
                    gear = gearInts[0];
                    if (gear < -10 || gear > 100) gear = 0;
                    if (gear > gearMax) gearMax = gear;

                    // Get rpm value
                    float[] rpmFloats = new float[1];
                    Buffer.BlockCopy(readBufferRpm, 0, rpmFloats, 0, readBufferRpm.Length);
                    rpm = rpmFloats[0];
                    if (rpm < 0.0f || rpm > 20000.0f) rpm = 0.0f;
                    if (rpm < rpmMin) rpmMin = rpm;
                    if (rpm > rpmMax) rpmMax = rpm;

                    // Get clutch value
                    float[] clutchFloats = new float[1];
                    Buffer.BlockCopy(readBufferClutch, 0, clutchFloats, 0, readBufferClutch.Length);
                    clutch = clutchFloats[0];
                    if (clutch < 0.0f || clutch > 1.0f) clutch = 0.0f;

                    ProcessTransform(newTransform, frameRateSecs);
                }
                catch (Exception e)
                {
                    Thread.Sleep(1000);
                }

            }
            reader.CloseHandle();

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

            rawData.gear = gear;
            rawData.max_gears = gearMax;

            rawData.idle_rpm = rpmMin;
            rawData.max_rpm = rpmMax;

            rawData.clutch_input = clutch;
        }

        public override void ProcessInputs()
        {
            base.ProcessInputs();

            rawData.engine_rate = rpm;
            filteredData.engine_rate = rpm;
        }

        void scan_ScanProgressChanged(object sender, ScanProgressChangedEventArgs e)
        {
            ui.ProgressBarChanged(e.Progress);
        }

        void scan_ScanCanceled(object sender, ScanCanceledEventArgs e)
        {
            ui.InitButtonStatusChanged(true);
        }

        void scan_ScanCompletedEngineData(object sender, ScanCompletedEventArgs e)
        {
            ui.InitButtonStatusChanged(true);

            if (e.MemoryAddresses == null || e.MemoryAddresses.Length == 0)
            {
                Utils.DebugLog("Wreckfest - Failed to find EngineData pattern");
                ui.StatusTextChanged("Failed2!");
                return;
            }
            Utils.DebugLog("Wreckfest - Found EngineData pattern at: " + e.MemoryAddresses[0].ToString("X"));

            memoryAddressEngineData = e.MemoryAddresses[0] - 0x70; //offset backwards to start of EngineData struct
            Utils.DebugLog("Wreckfest - Found engine data value at: " + memoryAddressEngineData.ToString("X"));
            memoryAddressGear = memoryAddressEngineData + 0xE98; //offset forwards to gear value
            Utils.DebugLog("Wreckfest - Found gear value at: " + memoryAddressGear.ToString("X"));
            memoryAddressRpm = memoryAddressEngineData + 0xB94; //offset forwards to rpm value
            Utils.DebugLog("Wreckfest - Found rpm value at: " + memoryAddressRpm.ToString("X"));
            memoryAddressClutch = memoryAddressEngineData + 0xE88; //offset forwards to clutch value
            Utils.DebugLog("Wreckfest - Found clutch value at: " + memoryAddressClutch.ToString("X"));

            ui.StatusTextChanged("Success2");

            t = new Thread(ScanComplete);
            t.IsBackground = true;
            t.Start();
        }

        void scan_ScanCompleted(object sender, ScanCompletedEventArgs e)
        {
            ui.InitButtonStatusChanged(true);

            if (e.MemoryAddresses == null || e.MemoryAddresses.Length == 0)
            {
                Utils.DebugLog("Wreckfest - Failed to find Matrix pattern");
                ui.StatusTextChanged("Failed1!");
                return;
            }
            Utils.DebugLog("Wreckfest - Found Matrix pattern at: " + e.MemoryAddresses[0].ToString("X"));

            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 4); //offset backwards from found address to start of matrix
            //            memoryAddress = e.MemoryAddresses[0] - ((4 * 4 * 4) + 8); //offset backwards from found address to start of matrix
            memoryAddressMatrix = e.MemoryAddresses[0] - (((4 * 4 * 4) * 2) + 8); //offset backwards from found address to start of matrix
            Utils.DebugLog("Wreckfest - Found matrix value at: " + memoryAddressMatrix.ToString("X"));

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
