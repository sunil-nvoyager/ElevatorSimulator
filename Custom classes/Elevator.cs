using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Timers;


namespace LiftSimulator
{
    public class Elevator
    {

        #region FIELDS

        private readonly object locker = new object();

        private Building myBuilding;

        private Floor currentFloor;
        private List<Floor> listOfFloorsToVisit;        
        private Direction elevatorDirection;
        private ElevatorStatus elevatorStatus;

        private int maximumPeopleInside;
        private List<Passenger> listOfPeopleInside;
        private bool IsFull;

        private Point elevatorPosition;
        private Bitmap[] elevatorFrames;
        private int currentFrameNumber;
        private int elevatorAnimationDelay;        
        private System.Timers.Timer elevatorTimer;

        #endregion


        #region METHODS

        public Elevator(Building Mybuilding, int HorizontalPosition, Floor StartingFloor)
        {
            
            this.myBuilding = Mybuilding;

            this.currentFloor = StartingFloor;
            this.listOfFloorsToVisit = new List<Floor>();
            this.elevatorDirection = Direction.None;
            this.elevatorStatus = ElevatorStatus.Idle;

            this.maximumPeopleInside = 2;
            this.listOfPeopleInside = new List<Passenger>();
            this.IsFull = false;

            this.elevatorPosition = new Point(HorizontalPosition, currentFloor.GetFloorLevelInPixels());
            currentFrameNumber = 0;
            elevatorFrames = new Bitmap[] 
            { 
                Properties.Resources.LiftDoors_Open, 
                Properties.Resources.LiftDoors_4, 
                Properties.Resources.LiftDoors_3,
                Properties.Resources.LiftDoors_2, 
                Properties.Resources.LiftDoors_1, 
                Properties.Resources.LiftDoors_Closed
            };
            this.elevatorAnimationDelay = 8;
            this.elevatorTimer = new System.Timers.Timer(6000); //set timer to 6 seconds
            this.elevatorTimer.Elapsed += new ElapsedEventHandler(this.Elevator_ElevatorTimerElapsed);

            this.PassengerEnteredTheElevator += new EventHandler(this.Elevator_PassengerEnteredTheElevator);
                        
            //Add new elevator to floor's list
            currentFloor.AddRemoveElevatorToTheListOfElevatorsWaitingHere(this, true);
            
        }

        /// <summary>
        /// Prepares the elevator to move to the next floor in the queue.
        /// </summary>
        /// <remarks>
        /// This method is responsible for setting the elevator's status to "PreparingForJob" and stopping the elevator's timer.
        /// It removes the elevator from the list of elevators waiting at the current floor, closes the elevator door, and then initiates the movement to the next floor on the list.
        /// This method can be invoked from either the ElevatorManager thread or the elevator's timer thread, ensuring that the elevator is ready for its next operation.
        /// </remarks>
        public void PrepareElevatorToGoToNextFloorOnTheList()
        {
            
            //Method can be invoked from ElevatorManager thread (SendAnElevator()) or elevator's timer thread (Elevator_ElevatorTimerElapsed())
                        
            //Update elevator's status
            SetElevatorStatus(ElevatorStatus.PreparingForJob);
            
            //Disable the timer
            this.elevatorTimer.Stop();

            //Remove this elevator from current floor's list
            currentFloor.AddRemoveElevatorToTheListOfElevatorsWaitingHere(this, false);
            
            //Close the door
            this.CloseTheDoor();            

            //Go!
            GoToNextFloorOnTheList();
            
        }

        /// <summary>
        /// Moves the elevator to the next floor in the list of floors to visit.
        /// </summary>
        /// <remarks>
        /// This method controls the movement of the elevator based on its current direction (up or down).
        /// It updates the elevator's status and graphic representation as it moves to the next floor.
        /// The current floor is updated, and the floor is removed from the list of floors to visit.
        /// The elevator's direction is also updated accordingly. 
        /// If there are passengers who want to get out at the current floor or if the elevator has reached its destination, 
        /// the method finalizes the movement to the next floor. 
        /// If the elevator is not full, it checks if it should stop at the current floor based on the lamp indicators. 
        /// If none of these conditions are met, the elevator continues to the next floor in the list.
        /// </remarks>
        private void GoToNextFloorOnTheList()
        {
            
            //Move control on the UI                 
            if (elevatorDirection == Direction.Down) //move down
            {
                this.SetElevatorStatus(ElevatorStatus.GoingDown);
                this.MoveTheElevatorGraphicDown(GetNextFloorToVisit().GetFloorLevelInPixels());
            }
            else if (elevatorDirection == Direction.Up) //move up
            {
                this.SetElevatorStatus(ElevatorStatus.GoingUp);
                this.MoveTheElevatorGraphicUp(GetNextFloorToVisit().GetFloorLevelInPixels());
            }

            //Update currentFloor
            this.currentFloor = GetNextFloorToVisit();

            //Remove current floor from the list of floors to visit
            this.listOfFloorsToVisit.RemoveAt(0);

            //Update elevator's direction
            UpdateElevatorDirection();

            //If one of passengers inside wants to get out here or this is end of the road,
            //then finalize going to next floor on the list
            if (SomePassengersWantsToGetOutOnThisFloor() || (this.elevatorDirection == Direction.None))
            {
                FinalizeGoingToNextFloorOnTheList();
                return;
            }

            //If elevator is not full, then check lamps on the floor
            if (!this.IsFull)
            {
                if (((this.elevatorDirection == Direction.Up) && (currentFloor.LampUp)) ||
                ((this.elevatorDirection == Direction.Down) && (currentFloor.LampDown)))
                {
                    FinalizeGoingToNextFloorOnTheList();
                    return;
                }
            }
            
            //If elevator doesn't stop here, let it go to next floor
            GoToNextFloorOnTheList();    
            
        }

        /// <summary>
        /// Finalizes the process of the elevator arriving at the next floor.
        /// </summary>
        /// <remarks>
        /// This method is responsible for several key actions that occur when the elevator reaches a new floor. 
        /// It first resets the appropriate lamp indicator for the current floor based on the elevator's direction (up, down, or none).
        /// After resetting the lamp, it opens the elevator door to allow passengers to enter and exit. 
        /// The elevator's status is then updated to indicate that it is waiting for passengers. 
        /// Each passenger inside the elevator is informed that they have reached their destination, and a delay is introduced to ensure that all passengers are visible as they exit. 
        /// The elevator is then added to the list of elevators waiting at the current floor, and an event is raised to notify any waiting passengers that the elevator has arrived or is no longer full. 
        /// Finally, a timer is started to manage the elevator's next actions.
        /// </remarks>
        private void FinalizeGoingToNextFloorOnTheList()
        {
            
            //Reset appropriate lamp on current floor
            switch (this.elevatorDirection)
            {
                case Direction.Up:
                    currentFloor.LampUp = false;
                    break;
                case Direction.Down:
                    currentFloor.LampDown = false;
                    break;
                case Direction.None:
                    currentFloor.LampUp = false;
                    currentFloor.LampDown = false;
                    break;
                default:
                    break;
            }
            
            //Open the door
            this.OpenTheDoor();

            //Update elevator's status
            SetElevatorStatus(ElevatorStatus.WaitingForPassengersToGetInAndGetOut);

            //Inform all passengers inside
            List<Passenger> PassengersInsideTheElevator = new List<Passenger>(listOfPeopleInside);
            foreach (Passenger SinglePassengerInsideTheElevator in PassengersInsideTheElevator)
            {
                SinglePassengerInsideTheElevator.ElevatorReachedNextFloor();
                Thread.Sleep(SinglePassengerInsideTheElevator.GetAnimationDelay() * 40); //to make sure all passengers will be visible when leaving the building
            }            

            //Add this elevator to next floor's list
            currentFloor.AddRemoveElevatorToTheListOfElevatorsWaitingHere(this, true);

            //Rise an event on current floor to inform passengers, who await
            currentFloor.OnElevatorHasArrivedOrIsNoteFullAnymore(new ElevatorEventArgs(this));

            //Enable the timer            
            this.elevatorTimer.Start();
            
        }
        
        /// <summary>
        /// Adds a new floor to the list of floors the elevator will visit.
        /// </summary>
        /// <param name="FloorToBeAdded">The floor to be added to the elevator's list of destinations.</param>
        /// <remarks>
        /// This method is designed to be thread-safe, using a lock to prevent concurrent modifications from different threads, such as the ElevatorManager or passenger threads.
        /// It first checks if the specified floor is already in the list of floors to visit; if it is, the method exits without making any changes.
        /// If the elevator is currently moving upwards, it adds all floors between the current floor and the specified floor to the list, ensuring that no duplicates are added.
        /// Conversely, if the elevator is moving downwards, it adds all floors in the downward direction to the list, again avoiding duplicates.
        /// Finally, it updates the direction of the elevator after modifying the list of floors.
        /// </remarks>
        public void AddNewFloorToTheList(Floor FloorToBeAdded)
        {
            
            lock (locker) //Method can be invoked from ElevatorManager thread (SendAnElevator()) or passenger's thread (AddNewPassengerIfPossible())
            {
                //If FloorToBeAdded is already on the list, do nothing
                if(GetListOfAllFloorsToVisit().Contains(FloorToBeAdded))
                {
                    return;
                }

                //If elevator is going up
                if (this.currentFloor.FloorIndex < FloorToBeAdded.FloorIndex)
                {
                    for (int i = this.currentFloor.FloorIndex + 1; i <= FloorToBeAdded.FloorIndex; i++)
                    {
                        if (!GetListOfAllFloorsToVisit().Contains(myBuilding.ArrayOfAllFloors[i]))
                        {
                            GetListOfAllFloorsToVisit().Add(myBuilding.ArrayOfAllFloors[i]);
                        }
                    }
                }

                //If elevator is going down
                if (this.currentFloor.FloorIndex > FloorToBeAdded.FloorIndex)
                {
                    for (int i = this.currentFloor.FloorIndex - 1; i >= FloorToBeAdded.FloorIndex; i--)
                    {
                        if (!GetListOfAllFloorsToVisit().Contains(myBuilding.ArrayOfAllFloors[i]))
                        {
                            this.GetListOfAllFloorsToVisit().Add(myBuilding.ArrayOfAllFloors[i]);
                        }
                    }
                }

                //Update ElevatorDirection
                UpdateElevatorDirection();                
            }
            
        }

        /// <summary>
        /// Determines if any passengers inside the elevator want to get out on the current floor.
        /// </summary>
        /// <returns>
        /// True if at least one passenger inside the elevator has a target floor that matches the current floor; otherwise, false.
        /// </returns>
        /// <remarks>
        /// This method iterates through a list of passengers currently inside the elevator, checking each passenger's target floor.
        /// If a passenger's target floor matches the elevator's current floor, the method returns true, indicating that at least one passenger wants to exit.
        /// If no passengers have the current floor as their target, the method returns false.
        /// This is useful for managing elevator stops and ensuring that the elevator only halts when necessary.
        /// </remarks>
        private bool SomePassengersWantsToGetOutOnThisFloor()
        {
            
            foreach (Passenger PassengerInsideThElevator in listOfPeopleInside)
            {
                if (PassengerInsideThElevator.GetTargetFloor() == this.currentFloor)
                {
                    return true;
                }                
            }
            return false;
            
        }

        public Floor GetCurrentFloor()
        {
            return currentFloor;
        }

        /// <summary>
        /// Retrieves the next floor to visit from the list of floors.
        /// </summary>
        /// <returns>The next <see cref="Floor"/> object to visit, or <c>null</c> if there are no floors to visit.</returns>
        /// <remarks>
        /// This method is designed to safely access the list of floors to visit by using a lock to prevent concurrent modifications.
        /// It checks if there are any floors in the <paramref name="listOfFloorsToVisit"/>. If there are, it returns the first floor in the list.
        /// If the list is empty, it returns <c>null</c>. This ensures that the method can be called safely from multiple threads without causing race conditions.
        /// </remarks>
        private Floor GetNextFloorToVisit()
        {
            
            lock (locker) //To avoid e.g. adding new element and checking whole list at the same time
            {
                if (listOfFloorsToVisit.Count > 0)
                {
                    return this.listOfFloorsToVisit[0];
                }
                else
                {
                    return null;
                }
            }
            
        }

        /// <summary>
        /// Retrieves the list of all floors that need to be visited.
        /// </summary>
        /// <returns>A list of <see cref="Floor"/> objects representing the floors to visit.</returns>
        /// <remarks>
        /// This method is thread-safe and uses a locking mechanism to ensure that the list of floors is not modified while it is being accessed.
        /// The lock prevents race conditions that could occur if one thread is adding a new floor while another thread is trying to retrieve the list.
        /// As a result, this method guarantees that the returned list is consistent and reflects the current state of the floors to visit.
        /// </remarks>
        public List<Floor> GetListOfAllFloorsToVisit()
        {
            
            lock (locker) //To avoid e.g. adding new element and checking whole list at the same time
            {
                return listOfFloorsToVisit;
            }
            
        }

        /// <summary>
        /// Updates the direction of the elevator based on the next floor to visit.
        /// </summary>
        /// <remarks>
        /// This method determines the elevator's direction by comparing the current floor with the next floor to visit.
        /// If there is no next floor to visit, the elevator direction is set to 'None'.
        /// If the current floor's index is less than the next floor's index, the direction is set to 'Up', indicating that the elevator should move upwards.
        /// Conversely, if the current floor's index is greater than or equal to the next floor's index, the direction is set to 'Down', indicating that the elevator should move downwards.
        /// This method does not require a lock since it is only referenced by the AddNewFloorToTheList method, which has its own locking mechanism.
        /// </remarks>
        private void UpdateElevatorDirection()
        {
            
            //Lock not needed:
            //AddNewFloorToTheList method is the only reference for this method and it has its own lock         
            if (GetNextFloorToVisit() == null)
            {
                this.elevatorDirection = Direction.None;
                return;
            }

            if (currentFloor.FloorIndex < GetNextFloorToVisit().FloorIndex)
            {
                this.elevatorDirection = Direction.Up;
            }
            else
            {
                this.elevatorDirection = Direction.Down;
            }     
            
        }

        /// <summary>
        /// Attempts to add a new passenger to the elevator if there is available space.
        /// </summary>
        /// <param name="NewPassenger">The passenger to be added to the elevator.</param>
        /// <param name="TargetFloor">The floor where the passenger intends to go.</param>
        /// <returns>True if the passenger was added successfully; otherwise, false.</returns>
        /// <remarks>
        /// This method checks if the elevator is not full and is in a suitable status (either idle or waiting for passengers).
        /// If these conditions are met, it resets the elevator timer to allow the new passenger time to board.
        /// The new passenger is then added to the list of people inside the elevator, and the target floor is recorded.
        /// If adding this passenger causes the elevator to reach its maximum capacity, it sets the elevator's status to 
        /// "PreparingForJob" to prevent any further passengers from attempting to board.
        /// If the elevator is full or not in an appropriate state, the method will return false, indicating that the 
        /// new passenger could not be added.
        /// </remarks>
        public bool AddNewPassengerIfPossible(Passenger NewPassenger, Floor TargetFloor)
        {
            
            //Passengers are added synchronically. Lock not needed.

            if (!IsFull && //check, if there is a place for another passenger
                ((GetElevatorStatus() == ElevatorStatus.Idle) || (GetElevatorStatus() == ElevatorStatus.WaitingForPassengersToGetInAndGetOut)))
            {
                //Reset elevator timer, so the passenger has time to get in
                this.ResetElevatorTimer();

                this.listOfPeopleInside.Add(NewPassenger); //add new passenger
                this.AddNewFloorToTheList(TargetFloor); //add new floor                    
                if (this.listOfPeopleInside.Count >= this.maximumPeopleInside) //set flag, if needed
                {
                    this.IsFull = true;
                    this.SetElevatorStatus(ElevatorStatus.PreparingForJob); // to prevent other passengers attempt to get in
                }

                return true; //new passenger added successfully
            }
            else
                return false; //new passenger not added due to lack of space in the elevator
            
        }

        /// <summary>
        /// Removes a passenger from the list of people inside.
        /// </summary>
        /// <param name="PassengerToRemove">The passenger to be removed from the list.</param>
        /// <remarks>
        /// This method is designed to safely remove a passenger from the internal list of people inside by using a lock to prevent concurrent modifications.
        /// It ensures that even if multiple passengers attempt to remove themselves at the same time, the operation will be thread-safe.
        /// After removing the specified passenger, it also sets the <see cref="IsFull"/> property to false, indicating that the capacity is no longer full.
        /// This method does not return any value and modifies the state of the object directly.
        /// </remarks>
        public void RemovePassenger(Passenger PassengerToRemove)
        {
            
            lock (locker) //Can be invoked by multiple passengers at once
            {
                this.listOfPeopleInside.Remove(PassengerToRemove);
                this.IsFull = false;
            }
            
        }

        /// <summary>
        /// Resets the elevator timer by stopping and then starting it again.
        /// </summary>
        /// <remarks>
        /// This method is designed to be thread-safe by using a lock on the <paramref name="locker"/> object.
        /// When called, it first stops the current elevator timer, which may be running, and then immediately restarts it.
        /// This is useful in scenarios where the elevator's timer needs to be refreshed, such as when the elevator is called or when it reaches a certain floor.
        /// The use of locking ensures that the timer reset operation is not interrupted by other threads, maintaining the integrity of the timer state.
        /// </remarks>
        public void ResetElevatorTimer()
        {
            
            lock (locker)
            {
                this.elevatorTimer.Stop();
                this.elevatorTimer.Start();
            }
            
        }

        /// <summary>
        /// Moves the elevator graphic down to the specified destination level.
        /// </summary>
        /// <param name="DestinationLevel">The level to which the elevator graphic should move down.</param>
        /// <remarks>
        /// This method animates the movement of the elevator graphic by updating its position 
        /// from the current Y position to the specified <paramref name="DestinationLevel"/>. 
        /// It uses a loop to incrementally change the Y position of the elevator, simulating 
        /// a downward movement. The animation delay is controlled by the <see cref="elevatorAnimationDelay"/> 
        /// property, which determines how long the thread sleeps between each position update. 
        /// This creates a smooth visual effect as the elevator moves downwards.
        /// </remarks>
        private void MoveTheElevatorGraphicDown(int DestinationLevel)
        {
            
            for (int i = this.GetElevatorYPosition(); i <= DestinationLevel; i++)
            {
                Thread.Sleep(this.elevatorAnimationDelay);
                this.elevatorPosition = new Point(GetElevatorXPosition(), i);
            }
            
        }

        /// <summary>
        /// Moves the elevator graphic upwards to the specified destination level.
        /// </summary>
        /// <param name="DestinationLevel">The level to which the elevator graphic should move.</param>
        /// <remarks>
        /// This method animates the movement of the elevator graphic by updating its position in a loop.
        /// It starts from the current Y position of the elevator and decrements the Y coordinate until it reaches the specified <paramref name="DestinationLevel"/>.
        /// The animation is controlled by a delay defined by <see cref="elevatorAnimationDelay"/>, which pauses the thread between position updates to create a smooth visual effect.
        /// This method does not return any value and modifies the elevator's position directly.
        /// </remarks>
        private void MoveTheElevatorGraphicUp(int DestinationLevel)
        {
            
            for (int i = this.GetElevatorYPosition(); i >= DestinationLevel; i--)
            {
                Thread.Sleep(this.elevatorAnimationDelay);
                this.elevatorPosition = new Point(GetElevatorXPosition(), i);
            }
            
        }

        /// <summary>
        /// Closes the door by transitioning through a series of frames.
        /// </summary>
        /// <remarks>
        /// This method simulates the closing of a door by iterating through five frames, 
        /// where each frame represents a stage in the closing process. The method uses 
        /// a loop to transition from one frame to the next, with a brief pause of 100 milliseconds 
        /// between each transition to simulate the time taken for the door to close. 
        /// The current frame number is updated sequentially from 0 to 5, indicating the 
        /// progression of the door closing action. This method does not return any value 
        /// and operates on the instance variable <c>currentFrameNumber</c>.
        /// </remarks>
        private void CloseTheDoor()
        {
            
            for (int i = 0; i < 5; i++)
            {
                switch (this.currentFrameNumber)
                {
                    case (0):
                        this.currentFrameNumber = 1;
                        Thread.Sleep(100);
                        break;
                    case(1):
                        this.currentFrameNumber = 2;
                        Thread.Sleep(100);
                        break;
                    case(2):
                        this.currentFrameNumber = 3;
                        Thread.Sleep(100);
                        break;
                    case(3):
                        this.currentFrameNumber = 4;
                        Thread.Sleep(100);
                        break;
                    case(4):
                        this.currentFrameNumber = 5;
                        Thread.Sleep(100);
                        break;
                }                
            }
            
        }

        /// <summary>
        /// Opens the door by transitioning through a series of frames.
        /// </summary>
        /// <remarks>
        /// This method simulates the process of opening a door by decrementing the <see cref="currentFrameNumber"/> 
        /// from 5 to 0 in a controlled manner. The method uses a loop that iterates five times, and during each 
        /// iteration, it checks the current frame number and decrements it accordingly. A brief pause is introduced 
        /// between each frame transition using <see cref="Thread.Sleep"/> to create a delay, simulating the 
        /// visual effect of the door opening. This method does not return any value and modifies the state of 
        /// the <see cref="currentFrameNumber"/> field directly.
        /// </remarks>
        private void OpenTheDoor()
        {
            
            for (int i = 0; i < 5; i++)
            {
                switch (this.currentFrameNumber)
                {
                    case (5):
                        this.currentFrameNumber = 4;
                        Thread.Sleep(100);
                        break;
                    case (4):
                        this.currentFrameNumber = 3;
                        Thread.Sleep(100);
                        break;
                    case (3):
                        this.currentFrameNumber = 2;
                        Thread.Sleep(100);
                        break;
                    case (2):
                        this.currentFrameNumber = 1;
                        Thread.Sleep(100);
                        break;
                    case (1):
                        this.currentFrameNumber = 0;
                        Thread.Sleep(100);
                        break;
                }
            }
            
        }

        public int GetElevatorXPosition()
        {
            return this.elevatorPosition.X;
        }

        public int GetElevatorYPosition()
        {
            return this.elevatorPosition.Y;
        }

        public Bitmap GetCurrentFrame()
        {
            return this.elevatorFrames[currentFrameNumber];
        }

        /// <summary>
        /// Retrieves the current status of the elevator.
        /// </summary>
        /// <returns>The current <see cref="ElevatorStatus"/> of the elevator.</returns>
        /// <remarks>
        /// This method is thread-safe and ensures that the elevator status is accessed in a controlled manner. 
        /// It uses a lock to prevent simultaneous modifications and reads of the elevator status, which could lead to inconsistent or incorrect data being returned. 
        /// By locking the access to the elevator status, this method guarantees that the returned value accurately reflects the state of the elevator at the time of the call.
        /// </remarks>
        public ElevatorStatus GetElevatorStatus()
        {
            
            lock (locker) //To avoid e.g. setting and getting status at the same time
            {
                return this.elevatorStatus;
            }
            
        }

        private void SetElevatorStatus(ElevatorStatus Status)
        {
            lock (locker) //To avoid e.g. setting and getting status at the same time
            {
                this.elevatorStatus = Status;
            }
        }

        public Direction GetElevatorDirection()
        {
            lock (locker) //To avoid reading during updating the elevatorDirection
            {
                return elevatorDirection;
            }
        }
        
        #endregion


        #region EVENTS

        public event EventHandler PassengerEnteredTheElevator;
        /// <summary>
        /// Invoked when a passenger enters the elevator.
        /// </summary>
        /// <param name="e">An instance of <see cref="PassengerEventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method raises the <see cref="PassengerEnteredTheElevator"/> event, notifying any subscribers that a passenger has entered the elevator.
        /// It first checks if there are any subscribers to the event by verifying if the event handler is not null.
        /// If there are subscribers, it invokes the event handler, passing the current instance and the event arguments.
        /// This allows for any additional logic or actions to be performed in response to the event by the subscribers.
        /// </remarks>
        public void OnPassengerEnteredTheElevator(PassengerEventArgs e)
        {
            
            EventHandler passengerEnteredTheElevator = PassengerEnteredTheElevator;
            if (passengerEnteredTheElevator != null)
            {
                passengerEnteredTheElevator(this, e);
            }
            
        }

        public event EventHandler ElevatorIsFull;
        /// <summary>
        /// Invokes the ElevatorIsFull event when the elevator is full and needs to go down.
        /// </summary>
        /// <param name="e">The event data associated with the elevator being full.</param>
        /// <remarks>
        /// This method checks if there are any subscribers to the ElevatorIsFull event and, if so, raises the event by invoking the delegate.
        /// The method takes an instance of <paramref name="e"/> which contains the event data that can be used by the event handlers.
        /// This is typically called when the elevator reaches its capacity and needs to descend, notifying all interested parties about this state change.
        /// </remarks>
        public void OnElevatorIsFullAndHasToGoDown(EventArgs e)
        {
            
            EventHandler elevatorIsFull = ElevatorIsFull;
            if (elevatorIsFull != null)
            {
                elevatorIsFull(this, e);
            }
            
        }

        #endregion


        #region EVENT HANDLERS

        public void Elevator_PassengerEnteredTheElevator(object sender, EventArgs e)
        {
            //Restart elevator's timer
            ResetElevatorTimer();
        }

        /// <summary>
        /// Handles the elapsed event of the elevator timer.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data containing the elapsed time information.</param>
        /// <remarks>
        /// This method is triggered when the elevator timer elapses. It checks if there is a next floor to visit by calling the 
        /// <see cref="GetNextFloorToVisit"/> method. If there are no more floors to visit, it stops the elevator timer and sets 
        /// the elevator status to idle. If there is a next floor, it prepares the elevator to go to that floor by calling 
        /// <see cref="PrepareElevatorToGoToNextFloorOnTheList"/>. This ensures that the elevator operates efficiently and 
        /// responds to the timer events appropriately.
        /// </remarks>
        public void Elevator_ElevatorTimerElapsed(object sender, ElapsedEventArgs e)
        {
            
            if (GetNextFloorToVisit() == null)
            {
                elevatorTimer.Stop();
                SetElevatorStatus(ElevatorStatus.Idle);                
            }
            else
            {
                //ThreadPool.QueueUserWorkItem(delegate { this.PrepareElevatorToGoToNextFloorOnTheList(); });                
                this.PrepareElevatorToGoToNextFloorOnTheList();
            }
            
        }

        #endregion

    }
}
