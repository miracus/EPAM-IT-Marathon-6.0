using System;

namespace APIHandler.Exceptions;

/// <summary>The public message stays generic; the detailed one is kept for logs.</summary>
public class ReservationNotFoundException(string message) : ValidationException("Reservation not found.")
{
    public string Details { get; } = message;
}