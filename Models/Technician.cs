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

    public virtual ICollection<Labour> Labours { get; set; } = new List<Labour>();

    public virtual Person Person { get; set; }
}