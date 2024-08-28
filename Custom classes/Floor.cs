using System;
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
        /// This method iterates through an array representing people waiting for an elevator.
        /// It checks each position in the array to find the first slot that is <c>null</c>, indicating that it is free.
        /// If a free slot is found, the index of that slot is returned.
        /// If no free slots are available, the method returns <c>null</c>.
        /// This method assumes that the array has been properly initialized and that it represents the current state of the queue.
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
        /// This method manages the queue of passengers waiting for an elevator. If the <paramref name="AddFlag"/> is true, it first checks for a free slot in the queue.
        /// If a free slot is available, it adds the specified <paramref name="PassengerToAddOrRemvove"/> to the queue and updates the passenger's position
        /// for display in the user interface. Additionally, it adds the passenger to the building's list of people needing animation.
        /// If the <paramref name="AddFlag"/> is false, it removes the specified passenger from the queue by setting their slot to null.
        /// This method assumes that the necessary locking mechanisms are already in place, as it does not implement any additional locking.
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
        /// Adds or removes an elevator from the list of elevators waiting here.
        /// </summary>
        /// <param name="ElevatorToAddOrRemove">The elevator to be added or removed from the list.</param>
        /// <param name="AddFlag">A boolean flag indicating whether to add (true) or remove (false) the elevator.</param>
        /// <remarks>
        /// This method manages the list of elevators waiting at a particular location. It ensures thread safety by using a lock to prevent multiple elevators from modifying the list simultaneously.
        ///
        /// When adding an elevator, it not only adds the elevator to the list but also subscribes to an event that is triggered when a passenger enters the elevator.
        /// Conversely, when removing an elevator, it unsubscribes from the event to avoid potential memory leaks or unintended behavior.
        ///
        /// The method is designed to handle concurrent access, ensuring that the operations on the list are atomic and consistent.
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
        /// <remarks>
        /// This method counts the number of non-null entries in the <paramref name="arrayOfPeopleWaitingForElevator"/>
        /// array, which represents the people waiting for the elevator. It uses a lock to ensure thread safety,
        /// preventing race conditions when accessing shared resources. The counting process iterates through
        /// the array up to the specified <paramref name="maximumAmmountOfPeopleInTheQueue"/> limit, incrementing
        /// a counter for each non-null entry found. The final count is returned as the result.
        /// </remarks>
        /// <returns>The current amount of people in the queue for the elevator.</returns>
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
        /// This allows any subscribed event handlers to respond to the occurrence of a new passenger.
        /// It is important to ensure that event handlers are properly subscribed to avoid null reference exceptions.
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
        /// This method checks if there are any subscribers to the event <c>ElevatorHasArrivedOrIsNotFullAnymore</c>.
        /// If there are subscribers, it raises the event, passing the current instance and the provided event arguments.
        /// This allows any registered event handlers to respond to the elevator's arrival or its capacity status.
        /// It is important to ensure that the event is not null before invoking it to avoid runtime exceptions.
        /// </remarks>
        public void OnElevatorHasArrivedOrIsNoteFullAnymore(ElevatorEventArgs e)
        {
            
            EventHandler elevatorHasArrivedOrIsNoteFullAnymore = ElevatorHasArrivedOrIsNotFullAnymore;
            if (elevatorHasArrivedOrIsNoteFullAnymore != null)
            {
                elevatorHasArrivedOrIsNoteFullAnymore(this, e);
            }
            
        }

        /// <summary>
        /// Handles the event when a new passenger appears.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data.</param>
        /// <remarks>
        /// This method is triggered when a new passenger event occurs. It first acquires a lock to ensure thread safety while processing the event.
        /// The method then unsubscribes from the event to prevent further handling, as it is no longer needed.
        /// It retrieves the new passenger information from the event arguments and calls the method <c>AddRemoveNewPassengerToTheQueue</c>
        /// to add the new passenger to the queue. The second parameter indicates that a new passenger is being added.
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
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data, specifically the passenger who entered.</param>
        /// <remarks>
        /// This method is triggered when a passenger enters the elevator. It locks access to ensure thread safety while processing the event.
        /// The method retrieves the passenger information from the event arguments and calls another method to remove the passenger from the queue.
        /// This is crucial for maintaining an accurate count of passengers currently in the elevator and managing their flow.
        /// The locking mechanism prevents race conditions in a multi-threaded environment, ensuring that only one thread can modify the queue at a time.
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
