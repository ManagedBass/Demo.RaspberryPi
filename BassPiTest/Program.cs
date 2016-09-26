using System;
using ManagedBass;
using System.Threading;

namespace BassPiTest
{
	/// <summary>
    /// Sample code for Bass on a Raspberry Pi.
    /// Original author: Logan Stromberg
    /// 
    /// For the most part, this code is fairly straightforward. The real difficulty is just in adapting the project to
    /// the type of hardware that you are using in your project, and making sure you can find the right libbass.so object.
    /// You must use the ARMv6 HARDFP build of Bass. Any other version will give you a cryptic "Could not find library: bass" error.
    /// </summary>
    public class MainClass
	{
        // Change these values based on your hardware configuration
        // Ideally we should autodetect this, but it's out of scope for a simple demo
        private const int PLAYBACK_SAMPLE_RATE = 8000;
        private const int RECORD_SAMPLE_RATE = 8000;

        /// <summary>
        /// Main program method
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main (string[] args)
		{
            Console.WriteLine("Initializing BASS audio...");

            // Try and load libbass.so
            // Normally, library search on linux will follow the behavior of ld.so,
            // defined here: http://man7.org/linux/man-pages/man8/ld.so.8.html
            // Basically, it will look in LD_LIBRARY PATH, /lib, and /usr/lib
            // If Bass is installed in any of those directories, you're fine
            // However, we can also override the path using the Load() method.
            // This allows us to load the library from the program's current directory,
            // which behavior is a little more familiar to how Windows loads DLLs
            if (!Bass.Load("."))
            {
                Console.WriteLine("Error: Could not find libbass.so in the program's current directory. Will check /usr/lib...");

                if (!Bass.Load())
                {
                    Console.WriteLine("libbass.so not found. This can also happen if the library is in the wrong format. Check that you are using the armv6-hf build");
                    return;
                }
            }

            Console.WriteLine("Success!");

			Console.WriteLine("Here are the audio out devices on this computer");
			for (int c = 0; c < Bass.DeviceCount; c++)
			{
				try
				{
					DeviceInfo info = Bass.GetDeviceInfo(c);
					Console.WriteLine("[{0}] Driver:{1} Name:{2} Type:{3}", c, info.Driver, info.Name, info.Type.ToString());
				}
				catch (Exception)
				{
				}
			}

            Console.WriteLine();
            Console.WriteLine("Which one should I use?");
            int playbackDevice = PromptUserForNumber();

            Console.WriteLine("Using playback device " + playbackDevice);
            Bass.Init(playbackDevice, PLAYBACK_SAMPLE_RATE);

            Console.WriteLine("Here are the recording devices on this computer");
            for (int c = 0; c < Bass.DeviceCount; c++)
            {
                try
                {
                    DeviceInfo info = Bass.RecordGetDeviceInfo (c);
                    Console.WriteLine("[{0}] Driver:{1} Name:{2} Type:{3}", c, info.Driver, info.Name, info.Type.ToString());
                }
                catch (Exception)
                {
                }
            }

            Console.WriteLine();
            Console.WriteLine("Which one should I use?");
            int recordDevice = PromptUserForNumber();

            Console.WriteLine("Using record device " + recordDevice);

            Bass.RecordInit(recordDevice);

			while (true)
			{
                Console.WriteLine("Press any key to start recording, or \'q\' to quit");
                if (Console.ReadKey().KeyChar == 'q')
                    break;

                Console.WriteLine("Getting record stream...");
                int hRecord = OpenHRecord(RECORD_SAMPLE_RATE);
                Console.WriteLine("--- Recording ---");
                short[] sample = DoRecord(hRecord, RECORD_SAMPLE_RATE);
                Console.WriteLine("--- Playing Back ---");
                DoPlayback(sample, RECORD_SAMPLE_RATE);
			}

			Bass.Stop();
            Bass.RecordFree();
			Bass.Free();
		}

        private static int PromptUserForNumber()
        {
            while (true)
            {
                string line = Console.ReadLine();
                int returnVal;
                if (int.TryParse(line, out returnVal))
                {
                    return returnVal;
                }

                Console.WriteLine("Sorry, you must enter a number");
            }
        }

        /// <summary>
        /// Begins recording on the default Bass device and returns an HRECORD handle to the device
        /// </summary>
        /// <returns></returns>
        private static int OpenHRecord(int recordSampleRate)
        {
            // Start recording on the device that was initialized earlier
            int hRecord = Bass.RecordStart(recordSampleRate, 1, BassFlags.Default, null);

            // A proper implementation would want to check the actual sample rate that the microphone is using,
            // rather than making assumptions; to do so you could use RecordGetInfo();
            // RecordInfo info;
            // Bass.RecordGetInfo(out info);

            return hRecord;
        }

        /// <summary>
        /// Records a 3-second sample from the given recording handle, and then closes it, returning the recorded bytes
        /// </summary>
        /// <param name="hRecord"></param>
        /// <returns></returns>
        private static short[] DoRecord(int hRecord, int sampleRate)
        {
            // We want to record 3 seconds of 16-bit audio
            int desiredSamples = RECORD_SAMPLE_RATE * 3;
            short[] finalSample = new short[desiredSamples];
            int samplesRecorded = 0;
            short[] scratchBuf = new short[(int)(Bass.RecordingBufferLength * (long)sampleRate / 1000)];
            while (samplesRecorded < desiredSamples)
            {
                // Query the number of bytes in the record buffer
                // Note that there's an implicit assumption that bytesAvailable will always be an even number
                // The buffer is ignored on this call, so we can just pass nullptr
                int bytesAvailable = Bass.ChannelGetData(hRecord, IntPtr.Zero, (int)DataFlags.Available);
                int samplesAvailable = bytesAvailable / 2;

                if (samplesAvailable != 0)
                {
                    // Read the data into the buffer
                    int bytesActuallyRead = Bass.ChannelGetData(hRecord, scratchBuf, 2 * Math.Min(scratchBuf.Length, samplesAvailable));
                    int samplesActuallyRead = bytesActuallyRead / 2;

                    // Write the input to the return sample
                    int samplesToUse = Math.Min(samplesActuallyRead, desiredSamples - samplesRecorded);
                    Array.Copy(scratchBuf, 0, finalSample, samplesRecorded, samplesToUse);
                    samplesRecorded += samplesToUse;
                }

                Thread.Sleep(10);
            }

            // Use ChannelStop on an HRECORD to stop recording
            Bass.ChannelStop(hRecord);

            return finalSample;
        }

        /// <summary>
        /// Plays back
        /// </summary>
        /// <param name="sample"></param>
        private static void DoPlayback(short[] sample, int sampleRate)
        {
            // Create a sample from the data that we recorded earlier.
            // Use the AUTOFREE flag so we don't have to do any cleanup
            // Alternatively we could just CreateStream() and then StreamPutData() and pass in the sample data,
            // but for short clips they're about the same thing
            int hSample = Bass.CreateSample(sample.Length * 2, sampleRate, 1, 1, BassFlags.AutoFree);
            Bass.SampleSetData(hSample, sample);
            int hChannel = Bass.SampleGetChannel(hSample);
            Bass.ChannelPlay(hChannel);

            while (Bass.ChannelIsActive(hChannel) == PlaybackState.Playing)
            {
                Thread.Sleep(10);
            }
        }
	}
}
