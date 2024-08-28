using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Drawing;

namespace LiftSimulator
{
    public class Passenger
    {
        #region FIELDS

        private readonly object locker = new object();

        static Bitmap[] ArrayOfAllPassengerGraphics = 
        {
            
            new Bitmap(Properties.Resources.TrollMan),
            new Bitmap(Properties.Resources.NoMan),
            new Bitmap(Properties.Resources.SheSmartass),
            new Bitmap(Properties.Resources.Smile),
            new Bitmap(Properties.Resources.Geek),
            new Bitmap(Properties.Resources.SheMad),
            new Bitmap(Properties.Resources.ForeverAlone),
            new Bitmap(Properties.Resources.SheSmile)
                
        };
        
        private Building myBuilding;

        private Floor currentFloor;
        private int currentFloorIndex;
        public Direction PassengerDirection;
        private PassengerStatus passengerStatus;  

        private Floor targetFloor;
        private int targetFloorIndex;

        public Point PassengerPosition;
        private Bitmap thisPassengerGraphic;
        private bool visible;
        private int passengerAnimationDelay;

        private Elevator myElevator;

        #endregion


        #region METHODS

        public Passenger(Building MyBuilding, Floor CurrentFloor, int TargetFloorIndex)
        {            
            
            this.myBuilding = MyBuilding;

            this.currentFloor = CurrentFloor;
            this.currentFloorIndex = CurrentFloor.FloorIndex;            
            this.passengerStatus = PassengerStatus.WaitingForAnElevator;

            this.targetFloor = MyBuilding.ArrayOfAllFloors[TargetFloorIndex];
            this.targetFloorIndex = TargetFloorIndex;

            this.PassengerPosition = new Point();

            Random random = new Random();
            this.thisPassengerGraphic = new Bitmap(Passenger.ArrayOfAllPassengerGraphics[random.Next(ArrayOfAllPassengerGraphics.Length)]);

            this.visible = true;
            this.passengerAnimationDelay = 8;

            //Subscribe to events
            this.currentFloor.NewPassengerAppeared += new EventHandler(currentFloor.Floor_NewPassengerAppeared);
            this.currentFloor.NewPassengerAppeared += new EventHandler(this.Passenger_NewPassengerAppeared);
            this.currentFloor.ElevatorHasArrivedOrIsNotFullAnymore += new EventHandler(this.Passenger_ElevatorHasArrivedOrIsNoteFullAnymore);
            
        }

        /// <summary>
        /// Finds an available elevator on the current floor or calls for a new one if none are available.
        /// </summary>
        /// <remarks>
        /// This method first updates the passenger's direction and retrieves a list of elevators currently waiting on the same floor.
        /// It then iterates through the list of elevators to find one that is either not moving or moving in the correct direction.
        /// If a suitable elevator is found, it attempts to add the passenger to that elevator. If successful, it updates the passenger's status
        /// to indicate they are getting into the elevator and initiates the process of entering the elevator on a separate thread.
        /// If no suitable elevator is found, it calls the elevator manager to request an elevator for the passenger based on their current floor
        /// and desired direction. This method ensures that the passenger is either accommodated by an existing elevator or that a new one is summoned.
        /// </remarks>
        private void FindAnElevatorOrCallForANewOne()
        {            
            
            UpdatePassengerDirection();

            //Copy the list of elevators available now on current floor
            List<Elevator> ListOfElevatorsWaitingOnMyFloor = currentFloor.GetListOfElevatorsWaitingHere();

            //Search the right elevator on my floor
            foreach (Elevator elevator in ListOfElevatorsWaitingOnMyFloor)
            {
                if (ElevatorsDirectionIsNoneOrOk(elevator))
                {
                    if (elevator.AddNewPassengerIfPossible(this, this.targetFloor))
                    {
                        //Update insideTheElevator
                        this.passengerStatus = PassengerStatus.GettingInToTheElevator;

                        ThreadPool.QueueUserWorkItem(delegate { GetInToTheElevator(elevator); });                        
                        return;
                    }
                }
            }

            //Call for an elevator
            myBuilding.ElevatorManager.PassengerNeedsAnElevator(currentFloor, this.PassengerDirection);   
            
        }

        /// <summary>
        /// Handles the process of a passenger entering the specified elevator.
        /// </summary>
        /// <param name="ElevatorToGetIn">The elevator that the passenger is entering.</param>
        /// <remarks>
        /// This method is responsible for managing the actions that occur when a passenger enters an elevator.
        /// It first raises an event to notify that a passenger has entered, passing the current passenger's details through
        /// the <see cref="PassengerEventArgs"/>. After notifying the event, it unsubscribes from the current floor's event
        /// indicating that the elevator has arrived or is no longer full. The method then updates the user interface by
        /// moving the passenger graphic to the appropriate position based on the elevator's current location.
        /// Finally, it sets the visibility of the passenger control to false and updates the reference to the current elevator
        /// that the passenger is in. This method ensures that all necessary updates and notifications are handled seamlessly
        /// when a passenger boards an elevator.
        /// </remarks>
        private void GetInToTheElevator(Elevator ElevatorToGetIn)
        {
            
            //Rise an event
            ElevatorToGetIn.OnPassengerEnteredTheElevator(new PassengerEventArgs(this));

            //Unsubscribe from an event for current floor
            this.currentFloor.ElevatorHasArrivedOrIsNotFullAnymore -= this.Passenger_ElevatorHasArrivedOrIsNoteFullAnymore;
            
            //Move the picture on the UI
            this.MovePassengersGraphicHorizontally(ElevatorToGetIn.GetElevatorXPosition());
            
            //Make PassengerControl invisible
            this.visible = false;
            
            //Update myElevator
            this.myElevator = ElevatorToGetIn;
            
        }

        /// <summary>
        /// Handles the event when the elevator reaches the next floor.
        /// </summary>
        /// <remarks>
        /// This method is invoked when the elevator arrives at a designated floor.
        /// It checks if the current floor of the elevator matches the target floor for the passengers inside.
        /// If they are at the correct floor, it sets the passenger status to indicate that they are leaving the building.
        /// Subsequently, it initiates the process for the passengers to exit the elevator by queuing a work item on the thread pool.
        /// This allows for asynchronous handling of the exit process without blocking the main thread.
        /// </remarks>
        public void ElevatorReachedNextFloor()
        {
            
            //For passengers, who are already inside an elevator:
            if (this.myElevator.GetCurrentFloor() == this.targetFloor)
            {
                //Set appropriate flag
                this.passengerStatus = PassengerStatus.LeavingTheBuilding;                

                //Get out of the elevator
                ThreadPool.QueueUserWorkItem(delegate { GetOutOfTheElevator(this.myElevator); });
            }
            
        }

        /// <summary>
        /// Handles the action of a passenger exiting the elevator.
        /// </summary>
        /// <param name="ElevatorWhichArrived">The elevator instance that has arrived for the passenger.</param>
        /// <remarks>
        /// This method is responsible for managing the process of a passenger getting out of the elevator.
        /// It first removes the passenger from the specified elevator by calling the <see cref="Elevator.RemovePassenger"/> method,
        /// ensuring that the passenger is no longer counted as being inside the elevator.
        /// After successfully removing the passenger, it invokes the <see cref="LeaveTheBuilding"/> method to handle
        /// the subsequent action of the passenger leaving the building.
        /// This method does not return any value and is intended to be called when a passenger has reached their desired floor.
        /// </remarks>
        private void GetOutOfTheElevator(Elevator ElevatorWhichArrived)
        {
            
            //Remove passenger from elevator
            ElevatorWhichArrived.RemovePassenger(this);

            //Leave the building
            this.LeaveTheBuilding();
            
        }

        /// <summary>
        /// Updates the direction of the passenger based on the current and target floor indices.
        /// </summary>
        /// <remarks>
        /// This method determines the direction in which the passenger should move based on their current floor index and the target floor index.
        /// If the current floor index is less than the target floor index, it sets the passenger direction to "Up".
        /// Otherwise, it sets the passenger direction to "Down". This method does not return any value and modifies the state of the object by updating the PassengerDirection property.
        /// </remarks>
        private void UpdatePassengerDirection()
        {
            
            if (currentFloorIndex < targetFloorIndex)
            {
                this.PassengerDirection = Direction.Up;
            }
            else
            {
                this.PassengerDirection = Direction.Down;
            }
            
        }

        /// <summary>
        /// Determines if the elevator's direction is either acceptable or not in use.
        /// </summary>
        /// <param name="ElevatorOnMyFloor">The elevator that is currently on the user's floor.</param>
        /// <returns>True if the elevator's direction matches the passenger's direction or if the elevator has no floors to visit; otherwise, false.</returns>
        /// <remarks>
        /// This method checks the direction of the specified elevator against the direction of the passenger.
        /// If the elevator is moving in the same direction as the passenger, or if it is not currently moving (i.e., has no floors to visit),
        /// the method returns true, indicating that the elevator is suitable for the passenger.
        /// If the elevator is moving in a different direction, the method returns false, indicating that it is not the right elevator for the passenger's needs.
        /// </remarks>
        private bool ElevatorsDirectionIsNoneOrOk(Elevator ElevatorOnMyFloor)
        {
            
            //Check if elevator has more floors to visit            
            if (ElevatorOnMyFloor.GetElevatorDirection() == this.PassengerDirection)
            {
                return true; //Elevator direction is OK
            }
            else if (ElevatorOnMyFloor.GetElevatorDirection() == Direction.None)
            {
                return true; //If an elevator has no floors to visit, then it is always the right elevator
            }

            return false; //Elevator direction is NOT OK
            
        }

        /// <summary>
        /// Handles the process of a passenger leaving the building.
        /// </summary>
        /// <remarks>
        /// This method updates the passenger's position to the elevator's current Y position,
        /// flips the passenger graphic horizontally for visual effect, and makes the passenger
        /// visible as they move towards the exit of the building. Once the passenger reaches
        /// the exit, they are made invisible again. The method also removes the passenger from
        /// the list of people who require animation, indicating that no further animation is needed
        /// for this passenger. Note that there is a TODO comment suggesting that disposing of
        /// the passenger object should be implemented in the future instead of simply making it invisible.
        /// </remarks>
        private void LeaveTheBuilding()
        {
            
            //Update starting position
            this.PassengerPosition = new Point(PassengerPosition.X, myElevator.GetElevatorYPosition());

            //Flip the control
            this.FlipPassengerGraphicHorizontally();

            //Make the passenger visible            
            this.visible = true;

            //Move the passenger up to the exit
            this.MovePassengersGraphicHorizontally(myBuilding.ExitLocation);

            //Make the passenger invisible again 
            //TO DO: dispose object instead making it invisble
            this.visible = false;

            //No need to animate it
            myBuilding.ListOfAllPeopleWhoNeedAnimation.Remove(this);
            
        }

        /// <summary>
        /// Moves the passenger graphic horizontally to the specified destination position.
        /// </summary>
        /// <param name="DestinationPosition">The target horizontal position to move the passenger graphic to.</param>
        /// <remarks>
        /// This method animates the movement of a passenger graphic by updating its horizontal position
        /// from the current position to the specified destination. The movement is performed in a
        /// stepwise manner, either to the left or right, depending on whether the current position is
        /// greater than or less than the destination position. The animation delay is controlled by
        /// the <see cref="passengerAnimationDelay"/> property, which determines how quickly the graphic
        /// moves. The method uses a loop to update the passenger's position incrementally, creating a
        /// smooth animation effect.
        /// </remarks>
        private void MovePassengersGraphicHorizontally (int DestinationPosition)
        {
            
            if (this.PassengerPosition.X > DestinationPosition) //go left
            {
                for (int i = this.PassengerPosition.X; i > DestinationPosition; i--)
                {
                    Thread.Sleep(this.passengerAnimationDelay);                    
                    this.PassengerPosition = new Point(i, this.PassengerPosition.Y);                    
                }
            }
            else //go right
            {
                for (int i = this.PassengerPosition.X; i < DestinationPosition; i++)
                {
                    Thread.Sleep(this.passengerAnimationDelay);
                    this.PassengerPosition = new Point(i, this.PassengerPosition.Y);
                }
            }
            
        }

        private void FlipPassengerGraphicHorizontally()
        {
            this.thisPassengerGraphic.RotateFlip(RotateFlipType.Rotate180FlipY);
        }             

        public Floor GetTargetFloor()
        {
            return this.targetFloor;
        }

        public bool GetPassengerVisibility()
        {
            return this.visible;
        }

        public int GetAnimationDelay()
        {
            return this.passengerAnimationDelay;
        }

        public Bitmap GetCurrentFrame()
        {
            return this.thisPassengerGraphic;
        }

        /// <summary>
        /// Handles the event when a new passenger appears on the current floor.
        /// </summary>
        /// <param name="sender">The source of the event, typically the floor where the passenger appeared.</param>
        /// <param name="e">An instance of <see cref="EventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method is triggered when a new passenger is detected on the current floor.
        /// It first unsubscribes from the <c>NewPassengerAppeared</c> event to prevent further handling of this event, as it is no longer needed.
        /// After unsubscribing, it proceeds to search for an available elevator or calls for a new one if none are available.
        /// This ensures that the system efficiently manages elevator requests and responds promptly to new passengers.
        /// </remarks>
        public void Passenger_NewPassengerAppeared(object sender, EventArgs e)
        {
            
            //Unsubscribe from this event (not needed anymore)            
            this.currentFloor.NewPassengerAppeared -= this.Passenger_NewPassengerAppeared;

            //Search an elevator
            FindAnElevatorOrCallForANewOne();  
            
        }

        /// <summary>
        /// Handles the event when a passenger elevator has arrived or is no longer full.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An instance of <see cref="ElevatorEventArgs"/> containing the elevator that raised the event.</param>
        /// <remarks>
        /// This method is triggered when an elevator arrives at a designated floor or when it has space for more passengers.
        /// It utilizes a locking mechanism to ensure that multiple elevators do not process this event simultaneously, preventing race conditions.
        ///
        /// The method first checks the current status of the passenger. If the passenger is in the process of getting into the elevator, the method returns early to avoid further processing.
        /// If the passenger is waiting for an elevator, it checks if the elevator's direction is appropriate and if it can accommodate a new passenger.
        /// If both conditions are met, it updates the passenger's status to indicate they are getting into the elevator and initiates the process of entering the elevator on a separate thread.
        /// If the elevator cannot accommodate the passenger, it calls for another elevator.
        /// </remarks>
        public void Passenger_ElevatorHasArrivedOrIsNoteFullAnymore(object sender, EventArgs e)
        {            
            
            lock (locker) //Few elevators (on different threads) can rise this event at the same time
            {
                Elevator ElevatorWhichRisedAnEvent = ((ElevatorEventArgs)e).ElevatorWhichRisedAnEvent;

                //For passengers who are getting in to the elevator and may not be able to unsubscribe yet                
                if (this.passengerStatus == PassengerStatus.GettingInToTheElevator)
                {
                    return;
                }

                //For passengers, who await for an elevator
                if (this.passengerStatus == PassengerStatus.WaitingForAnElevator)
                {
                    if ((ElevatorsDirectionIsNoneOrOk(ElevatorWhichRisedAnEvent) && (ElevatorWhichRisedAnEvent.AddNewPassengerIfPossible(this, targetFloor))))
                    {
                        //Set passengerStatus
                        passengerStatus = PassengerStatus.GettingInToTheElevator;
                        
                        //Get in to the elevator
                        ThreadPool.QueueUserWorkItem(delegate { GetInToTheElevator(ElevatorWhichRisedAnEvent); });
                    }
                    else
                    {
                        FindAnElevatorOrCallForANewOne();
                    }
                }                 
            }    
            
        }

        #endregion EVENT HANDLERS
    }
}
