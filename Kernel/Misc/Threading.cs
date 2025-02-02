/*
 * Copyright(c) 2022 nifanfa, This code is part of the Moos licensed under the MIT licence.
 */

using Internal.Runtime.CompilerServices;
using MOOS.Driver;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MOOS.Misc
{
    public unsafe class Thread
    {
        public bool Terminated;
        public IDT.IDTStackGeneric* Stack;
        public ulong SleepingTime;
        public int RunOnWhichCPU;

        public Thread(delegate*<void> method)
        {
            Stack = (IDT.IDTStackGeneric*)Allocator.Allocate((ulong)sizeof(IDT.IDTStackGeneric));

            Stack->irs.cs = 0x08;
            Stack->irs.ss = 0x10;
            const int Size = 16384;
            Stack->irs.rsp = ((ulong)Allocator.Allocate(Size)) + (Size);

            Stack->irs.rsp -= 8;
            *(ulong*)(Stack->irs.rsp) = (ulong)(delegate*<void>)&ThreadPool.Terminate;

            Stack->irs.rflags = 0x202;

            Stack->irs.rip = (ulong)method;

            Terminated = false;

            SleepingTime = 0;
        }

        public Thread Start()
        {
            //Bootstrap CPU
            this.RunOnWhichCPU = 0;
            ThreadPool.Threads.Add(this);
            return this;
        }

        public Thread Start(int run_on_which_cpu)
        {
            this.RunOnWhichCPU = run_on_which_cpu;
            ThreadPool.Threads.Add(this);
            return this;
        }

        public static void Sleep(ulong Millionsecos)
        {
            ThreadPool.Threads[ThreadPool.Index].SleepingTime = Millionsecos;
        }
    }

    internal static unsafe class ThreadPool
    {
        public static List<Thread> Threads;
        public static bool Initialized = false;
        public static bool Locked = false;
        public static long Locker = 0;

        internal static int Index
        {
            get
            {
                return Indexs[SMP.ThisCPU];
            }
            set
            {
                Indexs[SMP.ThisCPU] = value;
            }
        }

        public static void Initialize()
        {
            Native.Cli();
            //Bootstrap CPU
            if (SMP.ThisCPU == 0)
            {
                byte size = 0;
                for (int i = 0; i < ACPI.LocalAPIC_CPUIDs.Count; i++)
                    if (ACPI.LocalAPIC_CPUIDs[i] > size) size = ACPI.LocalAPIC_CPUIDs[i];
                Indexs = new int[size + 1];

                Locked = false;
                Initialized = false;
                Threads = new();
                //At least a thread for each CPU to make Thread Pool work
                new Thread(&IdleThread).Start();
                Initialized = true;
            }
            //Application CPU
            else
            {
                //At least a thread for each CPU to make Thread Pool work
                new Thread(&IdleThread).Start((int)SMP.ThisCPU);
            }
            Native.Sti();
            _int20h(); //start scheduling
        }

        public static void Terminate()
        {
            Console.Write("Thread ");
            Console.Write(Index.ToString());
            Console.WriteLine(" Has Exited");
            Threads[Index].Terminated = true;
            _int20h();
            Panic.Error("Termination Failed!");
        }

        [DllImport("*")]
        public static extern void _int20h();

        public static void TestThread()
        {
            Console.WriteLine("Non-Loop Thread Test!");
            return;
        }

        public static void A()
        {
            for (; ; ) Console.WriteLine("Thread A");
        }

        public static void B()
        {
            for (; ; ) Console.WriteLine("Thread B");
        }

        public static void IdleThread()
        {
            for (; ; ) Native.Hlt();
        }

        public static int[] Indexs;

        public static bool CanLock => Unsafe.As<bool, ulong>(ref Initialized);

        public static void Lock() 
        {
            Locker = SMP.ThisCPU;
            Locked = true;

            //LocalAPIC.SendAllInterrupt(0x20);
            //Warning: Thread.Sleep will be unaccurate if sending interrupt to CPU0(bootstrap CPU)
            LocalAPIC.SendAllInterruptIncludingSelf(0x20);
        }

        public static void UnLock() 
        {
            Locked = false;
        }

        public static int ThreadCount => Threads.Count;

        public static void Schedule(IDT.IDTStackGeneric* stack)
        {
            if (!Initialized) return;

            //Lock all processors except locker CPU
            if (Locked && Locker != SMP.ThisCPU)
            {
                while (Locked) Native.Nop();
                return;
            }

            //Lock locker CPU
            if (Locked && Locker == SMP.ThisCPU) return;

            if (SMP.ThisCPU == 0)
            {
                for (int i = 0; i < Threads.Count; i++)
                {
                    if (Threads[i].SleepingTime > 0)
                    {
                        Threads[i].SleepingTime--;
                    }
                }
            }

            for(; ; )
            {
                if (
                    !Threads[Index].Terminated &&
                    Threads[Index].RunOnWhichCPU == SMP.ThisCPU
                    )
                {
                    Native.Movsb(Threads[Index].Stack, stack, (ulong)sizeof(IDT.IDTStackGeneric));
                    break;
                }
                Index = (Index + 1) % Threads.Count;
            }

            do
            {
                Index = (Index + 1) % Threads.Count;
            } while
            (
                Threads[Index].Terminated ||
                (Threads[Index].SleepingTime > 0) ||
                Threads[Index].RunOnWhichCPU != SMP.ThisCPU
            );

            Native.Movsb(stack, Threads[Index].Stack, (ulong)sizeof(IDT.IDTStackGeneric));
        }
    }
}