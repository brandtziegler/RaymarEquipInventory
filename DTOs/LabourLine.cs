using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class LabourLine
    {
        public int LabourId { get; set; }
        public int SheetId { get; set; }
        public DateTime? DateofLabour { get; set; } // Assuming this can also be null

        private DateTime? _startLabour;
        public DateTime? StartLabour
        {
            get => _startLabour;
            set
            {
                _startLabour = value;
                CalculateTotalHoursAndMinutes(); // Recalculate when start time is set, if not null
            }
        }

        private DateTime? _finishLabour;
        public DateTime? FinishLabour
        {
            get => _finishLabour;
            set
            {
                _finishLabour = value;
                CalculateTotalHoursAndMinutes(); // Recalculate when finish time is set, if not null
            }
        }

        public double TotHours { get; private set; } = 0;
        public string TotTimeFormatted { get; private set; } = "00:00";

        public bool FlatRateJob { get; set; } = false;
        public string FlatRateJobDescription { get; set; } = "";
        public string WorkDescription { get; set; } = "";

        public int TechnicianID { get; set; } = 0;
        public string TechFirstName { get; set; } = ""; 
        public string TechLastName { get; set; } = "";
        public string TechFullName => $"{TechFirstName} {TechLastName}";
        public string TechPhone { get; set; } = "";
        public string TechEmail { get; set; } = "";


        // Method to calculate total hours and minutes
        private void CalculateTotalHoursAndMinutes()
        {
            if (_startLabour.HasValue && _finishLabour.HasValue && _finishLabour > _startLabour)
            {
                TimeSpan timeWorked = _finishLabour.Value - _startLabour.Value;
                TotHours = timeWorked.TotalHours;
                TotTimeFormatted = $"{(int)timeWorked.TotalHours:D2}:{timeWorked.Minutes:D2}";
            }
            else
            {
                TotHours = 0;
                TotTimeFormatted = "00:00";
            }
        }
    }


}
