﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class VehicleHistory
{
    public int VehicleHistoryId { get; set; }

    public int VehicleId { get; set; }

    public decimal? TravelTotal { get; set; }

    public virtual VehicleDatum Vehicle { get; set; }
}