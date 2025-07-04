﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class Technician
{
    public int TechnicianId { get; set; }

    public int PersonId { get; set; }

    public string Notes { get; set; }

    public string ShiftAvailibility { get; set; }

    public decimal? HourlyRate { get; set; }

    public string WorkStatus { get; set; }

    public virtual Person Person { get; set; }

    public virtual ICollection<TechnicianExperience> TechnicianExperiences { get; set; } = new List<TechnicianExperience>();

    public virtual ICollection<TechnicianLicence> TechnicianLicences { get; set; } = new List<TechnicianLicence>();

    public virtual ICollection<TechnicianWorkOrder> TechnicianWorkOrders { get; set; } = new List<TechnicianWorkOrder>();

    public virtual ICollection<WorkOrderSheet> WorkOrderSheets { get; set; } = new List<WorkOrderSheet>();
}