﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class DocumentType
{
    public int DocumentTypeId { get; set; }

    public string DocumentTypeName { get; set; }

    public string MimeType { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<InventoryDocument> InventoryDocuments { get; set; } = new List<InventoryDocument>();

    public virtual ICollection<PlaceholderDocument> PlaceholderDocuments { get; set; } = new List<PlaceholderDocument>();
}