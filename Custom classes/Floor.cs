﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;

namespace LiftSimulator
{
    public class Floor
    {
        #region FIELDS

        private readonly object locker = new object();

        private Building myBuilding;

        private int maximumAmmountOfPeopleInTheQueue; // depends on graphic

        private Passenger[] arrayOfPeopleWaitingForElevator;

        private List<Elevator> listOfElevatorsWaitingHere;

        private int floorIndex; //possible values for current graphic: 0, 1, 2, 3
        public int FloorIndex
        {
            get { return floorIndex; }
            private set { }
        }

        private int floorLevel; //determines (in pixels) where passengers should stand; depends on building graphic

        private int beginOfTheQueue; //determines, where queue of paasengers begins; depends on building graphic

        private int widthOfSlotForSinglePassenger; //ammount of pixels reserved for single passenger; depends on passenger graphic        

        public bool LampUp; //indicates, that at least one of passengers wants to up
        public bool LampDown; //indicates, that at least one of passengers wants to down

        #endregion


        #region METHODS

        public Floor(Building myBuilding, int floorNumber, int floorLevel)
        {
            
            this.myBuilding = myBuilding;

            maximumAmmountOfPeopleInTheQueue = 8; //only 8 passengers at once can be visible in current layout
            this.arrayOfPeopleWaitingForElevator = new Passenger[maximumAmmountOfPeopleInTheQueue];
            this.floorIndex = floorNumber;

            listOfElevatorsWaitingHere = new List<Elevator>();

            //Initialize variables, which depend on graphics:
            this.floorLevel = floorLevel;
            beginOfTheQueue = 367;
            widthOfSlotForSinglePassenger = 46;

            //Turn off both lamps
            LampUp = false;
            LampDown = false;
            
        }

        /// <summary>
        /// Finds the index of the first free slot in the queue.
        /// </summary>
        /// <returns>
        /// The index of the first free slot in the queue if one is available; otherwise, <c>null</c>.
        /// </returns>
        /// <remarks>
        /// This method iterates through the array of people waiting for the elevator, checking each slot to determine if it is free (i.e., contains a <c>null</c> value).
        /// The search continues until a free slot is found or all slots have been checked. If a free slot is found, its index is returned.
        /// If no free slots are available, the method returns <c>null</c>. This method assumes that the queue has a maximum capacity defined by <paramref name="maximumAmmountOfPeopleInTheQueue"/>.
        /// </remarks>
        private int? FindFirstFreeSlotInQueue()
        {
            
            //Lock not needed. Only one reference, already locked.
            for (int i = 0; i < maximumAmmountOfPeopleInTheQueue; i++)
            {
                if (arrayOfPeopleWaitingForElevator[i] == null)
                {
                    return i;
                }
            }

            return null;
            
        }

        /// <summary>
        /// Adds or removes a passenger from the queue based on the specified flag.
        /// </summary>
        /// <param name="PassengerToAddOrRemvove">The passenger to be added or removed from the queue.</param>
        /// <param name="AddFlag">A boolean flag indicating whether to add (true) or remove (false) the passenger.</param>
        /// <remarks>
        /// This method manages the queue of passengers waiting for the elevator. If the <paramref name="AddFlag"/> is true, 
        /// it first checks for an available slot in the queue using the <c>FindFirstFreeSlotInQueue</c> method. If a free slot 
        /// is found, it adds the specified <paramref name="PassengerToAddOrRemvove"/> to the queue and updates its position 
        /// for display in the user interface. Additionally, it adds the passenger to the building's list of people who need 
        /// animation. If the <paramref name="AddFlag"/> is false, it removes the specified passenger from the queue by 
        /// finding their index and setting that position in the array to null.
        /// </remarks>
        private void AddRemoveNewPassengerToTheQueue(Passenger PassengerToAddOrRemvove, bool AddFlag)
        {
            
            //Lock not needed. Only two references (from this), both already locked                        
            if (AddFlag) //Add passenger
            {
                int? FirstFreeSlotInQueue = FindFirstFreeSlotInQueue(); //Make sure there is a space to add new passenger
                if (FirstFreeSlotInQueue != null)
                {
                    //Add passenger object to an array                    
                    this.arrayOfPeopleWaitingForElevator[(int)FirstFreeSlotInQueue] = PassengerToAddOrRemvove;

                    //Add passenger control to the UI
                    int NewPassengerVerticalPosition = this.beginOfTheQueue + (this.widthOfSlotForSinglePassenger * (int)FirstFreeSlotInQueue);
                    PassengerToAddOrRemvove.PassengerPosition = new Point(NewPassengerVerticalPosition, GetFloorLevelInPixels());

                    //Add passenger to Building's list
                    myBuilding.ListOfAllPeopleWhoNeedAnimation.Add(PassengerToAddOrRemvove);
                }
            }
            else //Remove passenger
            {
                int PassengerToRemoveIndex = Array.IndexOf<Passenger>(GetArrayOfPeopleWaitingForElevator(), PassengerToAddOrRemvove);
                this.GetArrayOfPeopleWaitingForElevator()[PassengerToRemoveIndex] = null;
            }        
            
        }

        /// <summary>
        /// Adds or removes an elevator from the list of elevators waiting at the current location.
        /// </summary>
        /// <param name="ElevatorToAddOrRemove">The elevator to be added or removed from the waiting list.</param>
        /// <param name="AddFlag">A boolean flag indicating whether to add (true) or remove (false) the elevator.</param>
        /// <remarks>
        /// This method is designed to handle concurrent modifications to the list of waiting elevators by using a lock to ensure thread safety.
        /// When the <paramref name="AddFlag"/> is true, the specified elevator is added to the list of elevators waiting here,
        /// and it subscribes to an event that triggers when a passenger enters the elevator. Conversely, if <paramref name="AddFlag"/> is false,
        /// the elevator is removed from the list and unsubscribes from the passenger entry event. This ensures that the list remains accurate
        /// and that event handlers are properly managed to prevent memory leaks or unintended behavior.
        /// </remarks>
        public void AddRemoveElevatorToTheListOfElevatorsWaitingHere(Elevator ElevatorToAddOrRemove, bool AddFlag)
        {
            
            lock (locker) //Few elevators can try to add/remove themselfs at the same time
            {
                if (AddFlag) //Add elevator
                {
                    //Add elevator to the list
                    listOfElevatorsWaitingHere.Add(ElevatorToAddOrRemove);

                    //Subscribe to an event, rised when passenger entered the elevator
                    ElevatorToAddOrRemove.PassengerEnteredTheElevator += new EventHandler(this.Floor_PassengerEnteredTheElevator);
                }
                else //Remove elevator
                {
                    //Remove elevator from the list
                    listOfElevatorsWaitingHere.Remove(ElevatorToAddOrRemove);

                    //Unsubscribe from an event, rised when passenger entered the elevator
                    ElevatorToAddOrRemove.PassengerEnteredTheElevator -= this.Floor_PassengerEnteredTheElevator;
                }
            }
            
        }

        public int GetMaximumAmmountOfPeopleInTheQueue()
        {
            return maximumAmmountOfPeopleInTheQueue;
        }

        /// <summary>
        /// Retrieves the current number of people waiting in the queue for the elevator.
        /// </summary>
        /// <returns>The total count of people currently in the queue.</returns>
        /// <remarks>
        /// This method counts the number of non-null entries in the <paramref name="arrayOfPeopleWaitingForElevator"/> 
        /// up to the specified <paramref name="maximumAmmountOfPeopleInTheQueue"/>. It uses a lock to ensure thread safety 
        /// while accessing the shared resource, preventing race conditions that could occur when multiple threads 
        /// attempt to add or remove passengers from the queue simultaneously. The method iterates through the array, 
        /// incrementing a counter for each non-null entry, and returns the final count, which represents the current 
        /// number of people waiting for the elevator.
        /// </remarks>
        public int GetCurrentAmmountOfPeopleInTheQueue()
        {
            
            lock (locker) //The same lock is on add/remove passenger to the queue
            {
                int CurrentAmmountOfPeopleInTheQueue = 0;
                for (int i = 0; i < maximumAmmountOfPeopleInTheQueue; i++)
                {
                    if (this.arrayOfPeopleWaitingForElevator[i] != null)
                    {
                        CurrentAmmountOfPeopleInTheQueue++;
                    }
                }
                return CurrentAmmountOfPeopleInTheQueue;
            }
            
        }

        public Passenger[] GetArrayOfPeopleWaitingForElevator()
        {
            return arrayOfPeopleWaitingForElevator;
        }

        public List<Elevator> GetListOfElevatorsWaitingHere()
        {
            //Lock not needed. Method for passengers only.
            lock (locker)
            {
                return this.listOfElevatorsWaitingHere;
            }
        }

        public int GetFloorLevelInPixels()
        {
            return this.floorLevel;
        }

        #endregion


        #region EVENTS

        public event EventHandler NewPassengerAppeared;
        /// <summary>
        /// Raises the NewPassengerAppeared event when a new passenger appears.
        /// </summary>
        /// <param name="e">An instance of <see cref="EventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method checks if there are any subscribers to the NewPassengerAppeared event.
        /// If there are, it invokes the event, passing the current instance and the event data as parameters.
        /// This is typically used in scenarios where an event-driven architecture is implemented,
        /// allowing other parts of the application to respond to the occurrence of a new passenger.
        /// </remarks>
        public void OnNewPassengerAppeared(EventArgs e)
        {
            
            EventHandler newPassengerAppeared = NewPassengerAppeared;
            if (newPassengerAppeared != null)
            {
                newPassengerAppeared(this, e);
            }
            
        }

        public event EventHandler ElevatorHasArrivedOrIsNotFullAnymore;
        /// <summary>
        /// Invokes the event when the elevator has arrived or is no longer full.
        /// </summary>
        /// <param name="e">An instance of <see cref="ElevatorEventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method checks if there are any subscribers to the 
        /// <see cref="ElevatorHasArrivedOrIsNotFullAnymore"/> event. If there are, it raises the event, 
        /// passing the current instance and the provided event arguments. This allows any 
        /// registered event handlers to respond to the elevator's arrival or its status change 
        /// regarding fullness. It is essential for managing the state of the elevator system 
        /// and notifying other components of significant changes in its status.
        /// </remarks>
        public void OnElevatorHasArrivedOrIsNoteFullAnymore(ElevatorEventArgs e)
        {
            
            EventHandler elevatorHasArrivedOrIsNoteFullAnymore = ElevatorHasArrivedOrIsNotFullAnymore;
            if (elevatorHasArrivedOrIsNoteFullAnymore != null)
            {
                elevatorHasArrivedOrIsNoteFullAnymore(this, e);
            }
            
        }

        #endregion


        #region EVENT HADNLERS

        /// <summary>
        /// Handles the event when a new passenger appears.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data.</param>
        /// <remarks>
        /// This method is triggered when a new passenger event occurs. It first acquires a lock to ensure thread safety while processing the event.
        /// The method then unsubscribes from the event to prevent further handling, as it is no longer needed.
        /// It retrieves the new passenger information from the event arguments and calls the method <c>AddRemoveNewPassengerToTheQueue</c> 
        /// to add the new passenger to the queue. The second parameter indicates that the passenger is being added.
        /// </remarks>
        public void Floor_NewPassengerAppeared(object sender, EventArgs e)
        {
            
            lock (locker)
            {
                //Unsubscribe from this event (not needed anymore)
                this.NewPassengerAppeared -= this.Floor_NewPassengerAppeared;

                Passenger NewPassenger = ((PassengerEventArgs)e).PassengerWhoRisedAnEvent;

                AddRemoveNewPassengerToTheQueue(NewPassenger, true);
            }
            
        }

        /// <summary>
        /// Handles the event when a passenger enters the elevator.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data.</param>
        /// <remarks>
        /// This method is triggered when a passenger enters the elevator. It locks the critical section to ensure thread safety while processing the event.
        /// Inside the lock, it retrieves the passenger who triggered the event from the <paramref name="e"/> parameter.
        /// The method then calls <see cref="AddRemoveNewPassengerToTheQueue"/> to remove the passenger from the queue, indicating that they have entered the elevator.
        /// This ensures that the state of the queue is updated correctly to reflect the current passengers in the elevator.
        /// </remarks>
        public void Floor_PassengerEnteredTheElevator(object sender, EventArgs e)
        {
            
            lock (locker)
            {
                Passenger PassengerWhoEnteredOrLeftTheElevator = ((PassengerEventArgs)e).PassengerWhoRisedAnEvent;

                //Remove passenger from queue                
                AddRemoveNewPassengerToTheQueue(PassengerWhoEnteredOrLeftTheElevator, false);
            }
            
        }

        #endregion
    }
}
