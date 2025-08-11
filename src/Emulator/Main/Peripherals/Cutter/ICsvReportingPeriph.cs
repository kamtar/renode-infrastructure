namespace Antmicro.Renode.Peripherals.Cutter
{
    public interface ICsvReportingPeriph
    {
        /// <summary>
        /// Generates a CSV file containing the states of the peripheral.
        /// </summary>
        /// <param name="path">The path where the CSV file will be saved.</param>
        /// <remarks>
        /// The CSV file will contain the state of the peripheral at each point in time.
        /// </remarks>
        void GenerateCSVOfStates(string path);
    }
}