using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OuterSort
{
    /**
     *  Программа генерит файл из чисел double. Рандомизация правда не очень обширная - надо бы улучшить.
     *  Затем файл сортирует, не превышая заданный предел оперативной памяти (надо не более 100 Мб).
     *  Но точно задать ОП не получается - только опытным путем, оценивая реальные затраты при запуске программы.
     *  
     *  В итоге - лучший результат показала сортировка OuterShellSort:
     *  Файл 1 Гб - сортировка 29 минут, потребление памяти на 2 длинном шаге - около 70 Мб, в пиках 90 Мб,
     *  на 3 коротком шаге - около 120 Мб, в пиках 160 Мб.
     *  Используется только 1 процессор.
     *  Настройки: numbersCount = 64*1024*1024, blocksNum = 4*64
     *  
     *  Реализована параллельная сортировка Шелла (она нужна для загрузки всех процессоров - многопоточная), но
     *  тут используется в последовательном варианте, т.к. должен работать только 1 процессор.
     */
    class Program
    {
        static int numbersCount = 4*16*1024*1024;  //67108864 =  1 Гиг  , must be = 2^N
        static int blocksNum = 4*64;  // 4 - min , must be = 2^N
        static int blockSize;
        static int maxNumber = 1000; // don't used yet
        const int lineLength = 14;
        const int lineInBytes = 16;
        static string filePath = "..\\..\\..\\numbers.txt";
        static void Main(string[] args)
        {
            blockSize = numbersCount / blocksNum;
            double test = (double)numbersCount / blocksNum - blockSize;
            if (test != 0)
            {
                Console.WriteLine("Failure. Incorrect input data: numbersCount or blockNum!");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("//////////////////////////////////////////////");
            Console.WriteLine("///////////////  Outer sorting  //////////////");
            Console.WriteLine("//////////////////////////////////////////////\n");
            Console.WriteLine($"Double numbers count.............{numbersCount}");
            Console.WriteLine($"One number in file uses..........{FileSize(lineInBytes)}");
            Console.WriteLine($"File size........................{FileSize(numbersCount * lineInBytes)}");
            Console.WriteLine($"Number of blocks.................{blocksNum}");
            Console.WriteLine($"Block size.......................{blockSize} num / {FileSize(blockSize * lineInBytes)}");
            Console.WriteLine($"Max used RAM: blockSize * 4......{FileSize(blockSize * lineInBytes * 4)}");
            Console.WriteLine($"Used processors..................1");
            Console.WriteLine("----------------------------------------------\n");


            GenerateFile();

            Console.WriteLine();
            
            OuterShellSort(); // faster
            
            //OuterOddEvenSort(); // slower


            ///////////////////////////////////////////////////
            Console.WriteLine("\nfinish");
            Console.ReadKey();
        }


        //////////////////////////////////////////

        static void OuterShellSort()
        {
            Console.WriteLine("Shell sort working..");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            // PREPARE BLOCKS
            //int threadsNum = System.Environment.ProcessorCount;   // p = q / 2
            //int blocksNum = threadsNum * 2;    // q = 2^N
            //int blockSize = numbers.Count / blocksNum;
            List<KeyValuePair<int, int>> blocks = new List<KeyValuePair<int, int>>(blocksNum);
            for (int i = 0; i < blocksNum; i++)
            {
                int begin = i * blockSize;
                int end = begin + blockSize;
                if (i == blocksNum - 1)
                    end = numbersCount;
                blocks.Add(new KeyValuePair<int, int>(begin, end));
            }

            // STEP 1 - LOCAL SORT IN BLOCKS
            Console.WriteLine("Step 1: Local sorting in blocks started..");
            Console.Write($"Will be {blocksNum} steps: ");
            foreach (var block in blocks)
            {
                int begin = block.Key;
                int count = block.Value - block.Key;
                var numbers = ReadNumbersFromFile(begin, count);
                numbers.Sort();
                WriteNumbersToFile(numbers, begin);
                DisposeList(ref numbers);
                Console.Write("#");
            }
            Console.WriteLine();

            // STEP 2 - ITERATIONS MERGE-SPLIT FOR BLOCKS
            /*2 этап: N итераций merge-split для блоков
                на каждой i-итерации взаимодействуют блоки, номера которых
                различаются только в (N-i)-разряде в битовом представлении*/
            int iterNum = IntToBinaryString(blocks.Count - 1).TrimStart('0').Length; // iterNum = N
            Console.WriteLine("Step 2: Sorting all blocks started..");
            int steps2 = iterNum * blocksNum / 2;
            Console.Write($"Will be {steps2} steps: ");
            for (int i = 0; i < iterNum; i++)
            {
                int bit = iterNum - i;
                int mask = 1;
                mask <<= (bit - 1); // (N-i)b

                // MAKE BLOCK PAIRS
                List<KeyValuePair<int, int>> blockPairs = new List<KeyValuePair<int, int>>(blocksNum / 2);
                var blockItems = new LinkedList<int>(Enumerable.Range(0, blocksNum));
                while (blockItems.Count > 0)
                {
                    int first = blockItems.First();
                    blockItems.RemoveFirst();
                    foreach (var second in blockItems)
                    {
                        if ((first ^ second) == mask)
                        {
                            blockPairs.Add(new KeyValuePair<int, int>(first, second));
                            blockItems.Remove(second);
                            break;
                        }
                    }
                }

                // MERGE-SPLIT
                foreach (var blockPair in blockPairs)
                {
                    var firstBlock = blocks[blockPair.Key];
                    var secondBlock = blocks[blockPair.Value];
                    int resultSize = firstBlock.Value - firstBlock.Key + secondBlock.Value - secondBlock.Key;
                    List<double> result = new List<double>(resultSize);
                    var lNumbers = ReadNumbersFromFile(firstBlock.Key, firstBlock.Value - firstBlock.Key);
                    var rNumbers = ReadNumbersFromFile(secondBlock.Key, secondBlock.Value - secondBlock.Key);

                    //MERGE
                    int l = 0;
                    int lEnd = lNumbers.Count;
                    int r = 0;
                    int rEnd = rNumbers.Count;
                    while (l < lEnd && r < rEnd)
                    {
                        if (lNumbers[l] < rNumbers[r])
                        {
                            result.Add(lNumbers[l]);
                            l++;
                        }
                        else
                        {
                            result.Add(rNumbers[r]);
                            r++;
                        }
                    }
                    if (l < lEnd)
                    {
                        for (int n = l; n < lEnd; n++)
                            result.Add(lNumbers[n]);
                    }
                    if (r < rEnd)
                    {
                        for (int n = r; n < rEnd; n++)
                            result.Add(rNumbers[n]);
                    }

                    //SPLIT
                    int resultCounter = 0;
                    for (int f = 0; f < lNumbers.Count; f++)
                        lNumbers[f] = result[resultCounter++];

                    for (int s = 0; s < rNumbers.Count; s++)
                        rNumbers[s] = result[resultCounter++];
                    DisposeList(ref result);

                    // WRITE
                    WriteNumbersToFile(lNumbers, firstBlock.Key);
                    WriteNumbersToFile(rNumbers, secondBlock.Key);
                    DisposeList(ref lNumbers);
                    DisposeList(ref rNumbers);
                    Console.Write("#");
                }
            }
            Console.WriteLine();

            // STEP 3 - ODD-EVEN SORT
            // NEED TO EXIT WHEN WILL NO ANY CHANGES
            Console.WriteLine("Step 3: Final Odd-Even sorting all blocks started..");
            int steps3 = (blocksNum / 2) * (blocksNum / 2 + blocksNum / 2 - 1);
            int steps3min = blocksNum / 2 + blocksNum / 2 - 1;
            Console.Write($"Will be from {steps3min} to {steps3} steps: ");
            for (int it = 0; it < blocksNum / 2; it++)
            {
                // EVEN BLOCKS
                var evenSorted = Enumerable.Repeat(false, blocksNum / 2).ToList();
                for (int j = 0; j < blocksNum / 2; j++)
                {
                    int i = 2 * j;
                    var leftBlock = blocks[i];
                    var rightBlock = blocks[i + 1];
                    int doubleSize = rightBlock.Value - leftBlock.Key;
                    var numbers = ReadNumbersFromFile(leftBlock.Key, doubleSize);

                    // IS SORTED
                    bool allSorted = true;
                    for (int t = 0; t < numbers.Count - 1; t++)
                    {
                        if (numbers[t] > numbers[t + 1])
                        {
                            allSorted = false;
                            break;
                        }
                    }
                    if (allSorted)
                        evenSorted[j] = true;
                    else
                    {
                        // MERGE
                        List<double> result = new List<double>(doubleSize);
                        int l = 0;
                        int lEnd = leftBlock.Value - leftBlock.Key;
                        int r = lEnd;
                        int rEnd = r + rightBlock.Value - rightBlock.Key;
                        while (l < lEnd && r < rEnd)
                        {
                            if (numbers[l] < numbers[r])
                            {
                                result.Add(numbers[l]);
                                l++;
                            }
                            else
                            {
                                result.Add(numbers[r]);
                                r++;
                            }
                        }
                        if (l < lEnd)
                        {
                            for (int n = l; n < lEnd; n++)
                                result.Add(numbers[n]);
                        }
                        if (r < rEnd)
                        {
                            for (int n = r; n < rEnd; n++)
                                result.Add(numbers[n]);
                        }
                        DisposeList(ref numbers);

                        //COPY
                        WriteNumbersToFile(result, leftBlock.Key);
                        DisposeList(ref result); 
                    }
                    Console.Write("#");
                }

                // ODD BLOCKS
                var oddSorted = Enumerable.Repeat(false, blocksNum / 2 - 1).ToList();
                for (int j = 0; j < blocksNum / 2 - 1; j++)
                {
                    int i = 2 * j + 1;
                    var leftBlock = blocks[i];
                    var rightBlock = blocks[i + 1];
                    int doubleSize = rightBlock.Value - leftBlock.Key;
                    var numbers = ReadNumbersFromFile(leftBlock.Key, doubleSize);

                    // IS SORTED
                    bool allSorted = true;
                    for (int t = 0; t < numbers.Count - 1; t++)
                    {
                        if (numbers[t] > numbers[t + 1])
                        {
                            allSorted = false;
                            break;
                        }
                    }
                    if (allSorted)
                        oddSorted[j] = true;
                    else
                    {
                        // MERGE
                        List<double> result = new List<double>(doubleSize);
                        int l = 0;
                        int lEnd = leftBlock.Value - leftBlock.Key;
                        int r = lEnd;
                        int rEnd = r + rightBlock.Value - rightBlock.Key;
                        while (l < lEnd && r < rEnd)
                        {
                            if (numbers[l] < numbers[r])
                            {
                                result.Add(numbers[l]);
                                l++;
                            }
                            else
                            {
                                result.Add(numbers[r]);
                                r++;
                            }
                        }
                        if (l < lEnd)
                        {
                            for (int n = l; n < lEnd; n++)
                                result.Add(numbers[n]);
                        }
                        if (r < rEnd)
                        {
                            for (int n = r; n < rEnd; n++)
                                result.Add(numbers[n]);
                        }
                        DisposeList(ref numbers);

                        //COPY
                        WriteNumbersToFile(result, leftBlock.Key);
                        DisposeList(ref result);
                    }
                    Console.Write("#");
                }

                // ALL ARRAY IS SORTED
                var test = evenSorted.Union(oddSorted);
                if (!test.Contains(false))
                    break;
            }
            Console.WriteLine();
            watch.Stop();
            Console.WriteLine($"Sorting finished. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
        }
        static void OuterOddEvenSort()
        {
            Console.WriteLine("Odd-even sort working..");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            // PREPARE BLOCKS
            List<KeyValuePair<int, int>> blocks = new List<KeyValuePair<int, int>>(blocksNum);
            for (int i = 0; i < blocksNum; i++)
            {
                int begin = i * blockSize;
                int end = begin + blockSize;
                if (i == blocksNum - 1)
                    end = numbersCount;
                blocks.Add(new KeyValuePair<int, int>(begin, end));
            }
            // STEP 1 - LOCAL SORT IN BLOCKS
            Console.WriteLine("Local sorting all blocks started..");
            Console.Write($"Will be {blocksNum} steps: ");
            foreach (var block in blocks)
            {
                int begin = block.Key;
                int count = block.Value - block.Key;
                var numbers = ReadNumbersFromFile(begin, count);
                numbers.Sort();
                WriteNumbersToFile(numbers, begin);
                DisposeList(ref numbers);
                Console.Write("#");
            }
            Console.WriteLine("\nLocal sorting all blocks finished\n");

            // STEP 2 - ODD-EVEN BLOCK MERGE-SPLIT
            Console.WriteLine("Odd-even sorting all blocks started..");
            int oddEvenSteps = (blocksNum / 2) * (blocksNum / 2 + blocksNum / 2 - 1);
            Console.Write($"Will be {oddEvenSteps} steps: ");
            for (int it = 0; it < blocksNum / 2; it++)
            {
                // EVEN CHUNKS
                //Parallel.For(0, chunks.Count / 2, (j) =>
                for (int j = 0; j < blocksNum / 2; j++)
                {
                    int i = 2 * j;
                    var leftBlock = blocks[i];
                    var rightBlock = blocks[i + 1];
                    int doubleSize = rightBlock.Value - leftBlock.Key;
                    var numbers = ReadNumbersFromFile(leftBlock.Key, doubleSize);

                    // MERGE
                    List<double> result = new List<double>(doubleSize);
                    int l = 0;
                    int lEnd = leftBlock.Value - leftBlock.Key;
                    int r = lEnd;
                    int rEnd = r + rightBlock.Value - rightBlock.Key;
                    while (l < lEnd && r < rEnd)
                    {
                        if (numbers[l] < numbers[r])
                        {
                            result.Add(numbers[l]);
                            l++;
                        }
                        else
                        {
                            result.Add(numbers[r]);
                            r++;
                        }
                    }
                    if (l < lEnd)
                    {
                        for (int n = l; n < lEnd; n++)
                            result.Add(numbers[n]);
                    }
                    if (r < rEnd)
                    {
                        for (int n = r; n < rEnd; n++)
                            result.Add(numbers[n]);
                    }

                    //COPY
                    WriteNumbersToFile(result, leftBlock.Key);
                    DisposeList(ref numbers);
                    Console.Write("#");
                }


                // ODD CHUNKS
                //Parallel.For(0, blocksNum / 2 - 1, (j) =>
                for (int j = 0; j < blocksNum / 2 - 1; j++)
                {
                    int i = 2 * j + 1;
                    var leftBlock = blocks[i];
                    var rightBlock = blocks[i + 1];
                    int doubleSize = rightBlock.Value - leftBlock.Key;
                    var numbers = ReadNumbersFromFile(leftBlock.Key, doubleSize);

                    // MERGE
                    List<double> result = new List<double>(doubleSize);
                    int l = 0;
                    int lEnd = leftBlock.Value - leftBlock.Key;
                    int r = lEnd;
                    int rEnd = r + rightBlock.Value - rightBlock.Key;
                    while (l < lEnd && r < rEnd)
                    {
                        if (numbers[l] < numbers[r])
                        {
                            result.Add(numbers[l]);
                            l++;
                        }
                        else
                        {
                            result.Add(numbers[r]);
                            r++;
                        }
                    }
                    if (l < lEnd)
                    {
                        for (int n = l; n < lEnd; n++)
                            result.Add(numbers[n]);
                    }
                    if (r < rEnd)
                    {
                        for (int n = r; n < rEnd; n++)
                            result.Add(numbers[n]);
                    }

                    //COPY
                    WriteNumbersToFile(result, leftBlock.Key);
                    DisposeList(ref numbers);
                    Console.Write("#");
                }

            }
            Console.WriteLine();
            watch.Stop();
            Console.WriteLine($"Sorting finished. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
        }
        static void WriteNumbersToFile(List<double> numbers, int begin)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var number in numbers)
            {
                string strNumber = string.Format($"{number,-lineLength:e5}");
                sb.AppendLine(strNumber);
                //Console.WriteLine(strNumber + "_");
                //Console.WriteLine($"{number,-lineLength:e5}");
            }
            byte[] arrBytes = Encoding.Default.GetBytes(sb.ToString());
            try
            {
                using (FileStream fs = File.OpenWrite(filePath))
                {
                    fs.Seek(begin * lineInBytes, SeekOrigin.Begin);
                    fs.Write(arrBytes, 0, arrBytes.Length);
                }
                //Console.WriteLine($"FileStream : Numbers were written");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        static List<double> ReadNumbersFromFile(int begin, int count)
        {
            //Console.WriteLine("\nReading file ...\n");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            List<double> numbers = null;
            try
            {
                using (FileStream fs = File.OpenRead(filePath))
                {
                    fs.Seek(begin * lineInBytes, SeekOrigin.Begin);
                    byte[] arrBytes = new byte[lineInBytes * count];
                    fs.Read(arrBytes, 0, lineInBytes * count);
                    string text = Encoding.Default.GetString(arrBytes);
                    numbers = text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => double.Parse(s)).ToList();
                }
                watch.Stop();
                //Console.WriteLine($"Numbers were read. Time = {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return numbers;
        }
        static List<double> ReadNumbersFromFile_bad(int begin, int count)
        {
            //Console.WriteLine("\nReading file ...\n");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            List<double> numbers = new List<double>();
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line;
                    int lineCounter = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (lineCounter >= begin && lineCounter < begin + count)
                            numbers.Add(double.Parse(line.Trim()));
                        lineCounter++;
                    }
                }
                watch.Stop();
                //Console.WriteLine($"Numbers were read. Time = {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return numbers;
        }
        static void GenerateFile()
        {
            bool printProgress = false;
            int onePercent = numbersCount / 100;
            if (onePercent > 1)
                printProgress = true;
            Console.WriteLine("Writing file ...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            Random rand = new Random();
            try
            {
                using (StreamWriter sw = new StreamWriter(filePath, false))
                {
                    for (int i = 1; i <= numbersCount; i++)
                    {
                        //int baseValue = rand.Next(0, maxNumber);
                        double baseValue = rand.NextDouble(); // [0;1)
                        baseValue -= 0.5;
                        sw.WriteLine($"{baseValue,-lineLength:e5}");

                        if (printProgress && i % onePercent == 0)
                            Console.Write("#");
                    }
                    Console.WriteLine();
                }
                FileInfo file = new FileInfo(filePath);
                string size = FileSize(file.Length);

                watch.Stop();
                Console.WriteLine($"\nFile '{filePath}' was written, size = {size}. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        static string FileSize(long sizeBytes)
        {
            string size;
            if (sizeBytes < 1024)
                size = sizeBytes.ToString() + " B";
            else if (sizeBytes < 1024 * 1024)
                size = (sizeBytes / 1024).ToString() + " KB";
            else
                size = (sizeBytes / 1024 / 1024).ToString() + " MB";
            return size;
        }
        static List<int> ReadFileToList()
        {
            Console.WriteLine("\nReading file ...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            int onePercent = numbersCount / 10 / 100;
            onePercent = onePercent == 0 ? 1 : onePercent;
            List<int> numbers = new List<int>();
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line;
                    int lineCounter = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        numbers.AddRange(
                                line.Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .ToList()
                                    .Select(word => int.Parse(word))
                                    .ToList());
                        lineCounter++;
                        //Console.WriteLine(line);
                        if (lineCounter % onePercent == 0)
                            Console.Write("#");
                    }
                }
                watch.Stop();
                Console.WriteLine($"\nFile '{filePath}' was read. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return numbers;
        }
        static void ReadFile()
        {
            Console.WriteLine("\nReading file ...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            int onePercent = numbersCount / 10 / 100;
            onePercent = onePercent == 0 ? 1 : onePercent;
            try
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line;
                    int lineCounter = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lineCounter++;
                        //Console.WriteLine(line);
                        if (lineCounter % onePercent == 0)
                            Console.Write("#");
                    }
                }
                watch.Stop();
                Console.WriteLine($"\nFile '{filePath}' was read. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        static void WriteFile()
        {
            Console.WriteLine("Writing file ...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            Random rand = new Random();
            int onePercent = numbersCount / 100;
            try
            {
                using (StreamWriter sw = new StreamWriter(filePath, false))
                {
                    for (int i = 1; i < numbersCount + 1; i++)
                    {
                        int value = rand.Next(0, maxNumber + 1);
                        if (i % 10 != 0)
                            sw.Write($"{value,-lineLength} ");
                        else
                            sw.WriteLine($"{value,-lineLength}");
                        if (i % onePercent == 0)
                            Console.Write("#");
                    }
                }
                FileInfo file = new FileInfo(filePath);
                string size = FileSize(file.Length);

                watch.Stop();
                Console.WriteLine($"\nFile '{filePath}' was written, size = {size}. Time = {watch.ElapsedMilliseconds / 1000f:f2} sec.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        static List<int> GenerateList()
        {
            List<int> numbers = new List<int>(numbersCount);
            Random random = new Random();

            for (int i = 0; i < numbersCount; i++)
                numbers.Add(random.Next(0, maxNumber + 1));
            return numbers;
        }
        static void DisposeList<T>(ref List<T> numbers)
        {
            numbers = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        public static string IntToBinaryString(int number)
        {
            string result = "";
            for (int i = 0; i < 32; i++)
            {
                int mask = 1;
                mask = mask << i;
                int bitResult = number & mask;

                if (bitResult == 0)
                    result = "0" + result;
                else
                    result = "1" + result;
            }
            return result;
        }
    }

    public static class Extender
    {
        public static void Print(this List<int> list)
        {
            list.ForEach(n => Console.Write(n + " "));
            Console.WriteLine();
        }
    }
}
