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
        /// Attempts to find an available elevator on the current floor or calls for a new one if none are available.
        /// </summary>
        /// <remarks>
        /// This method first updates the direction of the passenger. It then retrieves a list of elevators currently waiting on the same floor.
        /// The method iterates through the list of elevators to find one that is either stationary or moving in the correct direction.
        /// If an appropriate elevator is found, it attempts to add the passenger to that elevator. If successful, it updates the passenger's status
        /// and initiates the process of getting into the elevator using a thread pool for asynchronous execution.
        /// If no suitable elevator is found, the method calls the elevator manager to request an elevator for the passenger.
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
        /// It raises an event to notify that a passenger has entered, unsubscribes from the current floor's event to avoid further notifications,
        /// updates the graphical representation of the passenger's movement on the user interface, and makes the passenger control invisible.
        /// Additionally, it updates the reference to the current elevator that the passenger is in.
        /// This ensures that the system accurately reflects the state of the elevator and the passenger's status.
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
        /// This method is triggered when the elevator arrives at a floor. It checks if the current floor of the elevator matches the target floor for the passengers inside.
        /// If they are at the correct floor, it sets the passenger status to indicate that they are leaving the building.
        /// Subsequently, it initiates the process for the passengers to exit the elevator by queuing a work item on the thread pool, which calls the
        /// <see cref="GetOutOfTheElevator"/> method to handle the exit procedure.
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
        /// <param name="ElevatorWhichArrived">The elevator instance from which the passenger is exiting.</param>
        /// <remarks>
        /// This method performs two main actions when a passenger exits the elevator.
        /// First, it removes the passenger from the specified elevator by calling the
        /// <see cref="Elevator.RemovePassenger"/> method. This ensures that the passenger
        /// is no longer counted as being inside the elevator.
        /// Second, it invokes the <see cref="LeaveTheBuilding"/> method to simulate
        /// the passenger leaving the building after exiting the elevator.
        /// This method does not return any value and is intended to be called
        /// when a passenger has reached their desired floor.
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
        /// This method checks the relationship between the current floor index and the target floor index.
        /// If the current floor index is less than the target floor index, it sets the passenger direction to "Up".
        /// Otherwise, it sets the passenger direction to "Down".
        /// This method is typically used in elevator systems to determine the movement direction of the elevator based on the requested floor.
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
        /// <returns>
        /// A boolean value indicating whether the elevator's direction is acceptable.
        /// Returns true if the elevator's direction matches the passenger's direction or if the elevator has no floors to visit.
        /// Returns false if the elevator's direction does not match and it is not idle.
        /// </returns>
        /// <remarks>
        /// This method checks the current direction of the specified elevator against the direction the passenger intends to go.
        /// If the elevator is moving in the same direction as the passenger or is not currently assigned to any floors (Direction.None),
        /// it is considered an appropriate choice for the passenger. If neither condition is met, the method indicates that the elevator
        /// is not suitable for the passenger's needs. This method helps in determining whether to call or wait for a specific elevator
        /// based on its current operational state.
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
        /// This method updates the passenger's position to the current Y position of the elevator,
        /// flips the passenger graphic horizontally to indicate movement, and makes the passenger visible
        /// as they move towards the exit of the building. Once the passenger reaches the exit,
        /// they are made invisible again, and their entry is removed from the list of people who need animation.
        /// It is important to note that there is a TODO comment indicating that disposing of the object
        /// should be considered instead of merely making it invisible.
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
        /// This method animates the movement of a passenger graphic by updating its position in a horizontal direction.
        /// If the current position of the passenger is greater than the destination position, the graphic will move left;
        /// otherwise, it will move right. The movement is animated with a delay specified by <see cref="passengerAnimationDelay"/>.
        /// The method uses a loop to incrementally update the passenger's position until it reaches the desired destination.
        /// This creates a smooth transition effect for the passenger graphic.
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
        /// <param name="sender">The source of the event, typically the current floor.</param>
        /// <param name="e">An instance of <see cref="EventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method is triggered when a new passenger is detected on the current floor.
        /// It first unsubscribes from the event to prevent further handling since this instance
        /// of the event is no longer needed. After unsubscribing, it proceeds to search for an
        /// available elevator or calls for a new one if none are available. This ensures that
        /// the system efficiently manages elevator requests and responds to passenger needs promptly.
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
        /// This method is invoked when an elevator arrives at a designated floor or when it has space for more passengers.
        /// It first locks the method to prevent concurrent access from multiple elevators that may trigger this event simultaneously.
        ///
        /// If the passenger is currently in the process of getting into the elevator, the method returns immediately to avoid any conflicts.
        ///
        /// For passengers who are waiting for an elevator, it checks if the elevator's direction is appropriate and if it can accommodate the new passenger.
        /// If both conditions are met, it updates the passenger's status to indicate they are getting into the elevator and initiates the process of entering the elevator on a separate thread.
        ///
        /// If the elevator cannot accommodate the passenger, it calls for another elevator or finds an alternative solution.
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
