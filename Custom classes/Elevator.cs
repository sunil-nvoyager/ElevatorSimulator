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
        /// Prepares the elevator to go to the next floor in the list.
        /// </summary>
        /// <remarks>
        /// This method is designed to be invoked from either the ElevatorManager thread (via the SendAnElevator() method)
        /// or the elevator's timer thread (via the Elevator_ElevatorTimerElapsed() method).
        /// It performs several critical operations to prepare the elevator for its next journey:
        ///
        /// 1. Updates the elevator's status to indicate that it is preparing for a job.
        /// 2. Stops the elevator timer to prevent any further actions until the elevator is ready.
        /// 3. Removes the elevator from the current floor's list of waiting elevators, indicating that it is no longer available at that floor.
        /// 4. Closes the elevator door to ensure safety during movement.
        /// 5. Initiates the movement of the elevator to the next floor in its list.
        ///
        /// This method does not return any value and modifies the state of the elevator and its associated components directly.
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
        /// It updates the elevator's status, moves the elevator graphic accordingly, and manages the list of floors to visit.
        ///
        /// The method first checks the direction of the elevator and updates its status and graphic position.
        /// It then updates the current floor and removes it from the list of floors to visit.
        /// The elevator's direction is also updated accordingly.
        ///
        /// If any passengers wish to exit on the current floor or if the elevator has reached the end of its route,
        /// it finalizes the movement to the next floor. If the elevator is not full, it checks for any lamp indicators
        /// on the current floor that may signal a stop. If no conditions are met for stopping, it recursively calls itself
        /// to continue moving to the next floor in the list.
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
        /// Finalizes the process of the elevator moving to the next floor.
        /// </summary>
        /// <remarks>
        /// This method is responsible for completing the transition of the elevator to the next floor.
        /// It performs several key actions:
        /// 1. It resets the appropriate lamp indicator for the current floor based on the elevator's direction (up, down, or none).
        /// 2. It opens the elevator door to allow passengers to enter and exit.
        /// 3. It updates the elevator's status to indicate that it is waiting for passengers.
        /// 4. It informs all passengers currently inside the elevator that they have reached the next floor,
        ///    allowing them to exit with a delay to ensure visibility.
        /// 5. It adds the elevator to the list of elevators waiting at the current floor.
        /// 6. It raises an event on the current floor to notify any waiting passengers that the elevator has arrived or is no longer full.
        /// 7. Finally, it starts the elevator timer to manage subsequent operations.
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
        /// Adds a new floor to the list of floors that the elevator will visit.
        /// </summary>
        /// <param name="FloorToBeAdded">The floor to be added to the list of floors to visit.</param>
        /// <remarks>
        /// This method is designed to be thread-safe, utilizing a lock to prevent concurrent modifications
        /// from different threads, such as the ElevatorManager thread or a passenger's thread.
        /// It first checks if the specified floor is already in the list of floors to visit; if it is,
        /// the method returns without making any changes. If the elevator is moving upwards, it adds
        /// all intermediate floors between the current floor and the target floor to the list. Conversely,
        /// if the elevator is moving downwards, it adds all intermediate floors in the opposite direction.
        /// After updating the list of floors, it calls the method to update the elevator's direction.
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
        /// <remarks>
        /// This method iterates through a list of passengers currently inside the elevator, checking each passenger's target floor.
        /// If any passenger's target floor matches the current floor of the elevator, the method returns true, indicating that at least one passenger wants to exit.
        /// If no passengers have the current floor as their target, the method returns false.
        /// This is useful for managing elevator stops and ensuring that the elevator operates efficiently by only stopping when necessary.
        /// </remarks>
        /// <returns>
        /// True if at least one passenger wants to get out on the current floor; otherwise, false.
        /// </returns>
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
        /// This method provides a thread-safe way to access the list of floors to visit by using a lock to prevent concurrent modifications.
        /// The lock ensures that while one thread is accessing the list, no other thread can modify it, thus avoiding potential race conditions.
        /// This method simply returns the current state of the list without making any modifications.
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
        /// This method determines the elevator's direction by comparing the current floor's index with the index of the next floor to visit.
        /// If there is no next floor to visit, the elevator direction is set to <see cref="Direction.None"/>.
        /// If the current floor is below the next floor, the direction is set to <see cref="Direction.Up"/>.
        /// If the current floor is above the next floor, the direction is set to <see cref="Direction.Down"/>.
        /// This method does not require a lock as it is only called by the <see cref="AddNewFloorToTheList"/> method, which manages its own locking mechanism.
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
        /// <param name="TargetFloor">The floor where the new passenger wants to go.</param>
        /// <returns>True if the new passenger was added successfully; otherwise, false.</returns>
        /// <remarks>
        /// This method checks if the elevator is not full and is either idle or waiting for passengers to get in or out.
        /// If these conditions are met, it resets the elevator timer to allow the passenger time to enter.
        /// The new passenger is then added to the list of people inside the elevator, and the target floor is added to the list of floors.
        /// If adding the new passenger causes the elevator to reach its maximum capacity, the elevator's status is updated to prevent further entries.
        /// This method ensures that the elevator operates smoothly and efficiently while managing its capacity.
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
        /// The lock ensures that even if multiple threads attempt to remove passengers at the same time, the operation will be thread-safe.
        /// After removing the specified passenger, the method also sets the <see cref="IsFull"/> property to false, indicating that the capacity is no longer full.
        /// This is important for managing the state of the system, especially in scenarios where the capacity of passengers is limited.
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
        /// Resets the elevator timer by stopping and restarting it.
        /// </summary>
        /// <remarks>
        /// This method is designed to reset the elevator timer, which is responsible for tracking the time
        /// the elevator has been idle. By stopping the timer and then immediately starting it again,
        /// this method effectively resets the timer to zero. The operation is performed within a lock
        /// to ensure thread safety, preventing race conditions that could occur if multiple threads
        /// attempt to reset the timer simultaneously. This is crucial in a multi-threaded environment
        /// where the elevator's operational state may be accessed or modified by different threads.
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
        /// This method animates the movement of the elevator graphic by updating its position in a loop.
        /// It starts from the current Y position of the elevator and increments the Y coordinate until it reaches the specified <paramref name="DestinationLevel"/>.
        /// During each iteration, the method pauses for a duration defined by <see cref="elevatorAnimationDelay"/> to create a smooth animation effect.
        /// The elevator's X position remains constant while the Y position is updated.
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
        /// It retrieves the current Y position of the elevator and decrements it until it reaches the specified <paramref name="DestinationLevel"/>.
        /// During each iteration, the method pauses for a duration defined by <see cref="elevatorAnimationDelay"/> to create a smooth animation effect.
        /// The elevator's X position remains constant while the Y position is updated to simulate upward movement.
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
        /// This method simulates the process of closing a door by iterating through a sequence of frames,
        /// represented by the variable <c>currentFrameNumber</r>. The method uses a loop to transition
        /// from one frame to the next, pausing for a short duration (100 milliseconds) between each transition.
        /// The loop runs a fixed number of times (5), allowing the door to move through all defined frames
        /// until it reaches the final state. This method does not return any value and modifies the
        /// <c>currentFrameNumber</c> property to reflect the current state of the door.
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
        /// Opens the door by decrementing the current frame number in a controlled manner.
        /// </summary>
        /// <remarks>
        /// This method simulates the process of opening a door by reducing the <paramref name="currentFrameNumber"/> from 5 to 0.
        /// It does this by iterating five times and using a switch statement to decrement the frame number in each iteration.
        /// After each decrement, the method pauses for 100 milliseconds to create a delay, simulating a mechanical movement.
        /// This method does not return any value and modifies the state of the <paramref name="currentFrameNumber"/> directly.
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
        /// This method safely returns the elevator's status by using a lock to prevent concurrent access issues.
        /// The lock ensures that while the status is being retrieved, no other thread can modify the elevator's status,
        /// thereby maintaining data integrity. This is particularly important in multi-threaded environments where
        /// multiple operations may attempt to read or write the elevator status simultaneously.
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
        /// Invokes the PassengerEnteredTheElevator event when a passenger enters the elevator.
        /// </summary>
        /// <param name="e">An instance of <see cref="PassengerEventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method checks if there are any subscribers to the PassengerEnteredTheElevator event.
        /// If there are, it raises the event, passing the current instance and the event arguments to the subscribers.
        /// This allows any registered event handlers to respond to the event of a passenger entering the elevator.
        /// It is important to ensure that event handlers are properly subscribed to avoid null reference exceptions.
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
        /// Notifies that the elevator is full and needs to go down.
        /// </summary>
        /// <param name="e">Event arguments associated with the elevator event.</param>
        /// <remarks>
        /// This method triggers the ElevatorIsFull event, indicating that the elevator has reached its capacity and is preparing to descend.
        /// It checks if there are any subscribers to the ElevatorIsFull event and, if so, invokes the event, passing the current instance and the event arguments.
        /// This allows any registered event handlers to respond appropriately when the elevator is full.
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
        /// Handles the event when the elevator timer elapses.
        /// </summary>
        /// <param name="sender">The source of the event, typically the timer that has elapsed.</param>
        /// <param name="e">An instance of <see cref="ElapsedEventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method checks if there is a next floor for the elevator to visit. If there is no next floor, it stops the elevator timer
        /// and sets the elevator status to idle. If there is a next floor, it prepares the elevator to go to that floor.
        /// The preparation process involves executing the <see cref="PrepareElevatorToGoToNextFloorOnTheList"/> method,
        /// which handles the logic for moving the elevator to the designated floor.
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
