﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class PartsDocument
{
    public int PartsDocumentId { get; set; }

    public int PartUsedId { get; set; }

    public int DocumentId { get; set; }

    public virtual Document Document { get; set; }

    public virtual PartsUsed PartUsed { get; set; }
}