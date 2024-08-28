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
        /// Finds an available elevator on the current floor or requests a new one if none are available.
        /// </summary>
        /// <remarks>
        /// This method first updates the direction of the passenger. It then retrieves a list of elevators that are currently waiting on the same floor.
        /// The method iterates through the list of elevators to find one that is either not in use or is moving in the correct direction.
        /// If an appropriate elevator is found, it attempts to add the passenger to that elevator. If successful, it updates the passenger's status to indicate they are getting into the elevator and initiates the process of entering the elevator on a separate thread.
        /// If no suitable elevator is found, the method calls for an elevator through the building's elevator manager, indicating the current floor and the desired direction of travel.
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
        /// It first raises an event to notify that a passenger has entered the elevator, passing along relevant event arguments.
        /// Then, it unsubscribes from the current floor's elevator arrival event to prevent further notifications while the passenger is in transit.
        /// The method also updates the user interface by moving the graphical representation of the passengers to the new position of the elevator.
        /// Finally, it makes the passenger control invisible and updates the reference to the current elevator for the passenger.
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
        /// If the floors match, it updates the passenger status to indicate that they are leaving the building. 
        /// Subsequently, it initiates the process for the passengers to exit the elevator by queuing a work item on the thread pool, which calls the method <c>GetOutOfTheElevator</c> with the current elevator instance as an argument.
        /// This allows for a non-blocking exit process, ensuring that the elevator can continue to operate smoothly without waiting for passengers to disembark.
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
        /// <see cref="Elevator.RemovePassenger"/> method, ensuring that the passenger 
        /// is no longer counted as being inside the elevator. 
        /// Second, it invokes the <see cref="LeaveTheBuilding"/> method to simulate 
        /// the passenger leaving the building after exiting the elevator. 
        /// This method does not return any value and is intended to be called 
        /// when a passenger has reached their desired floor and is ready to exit.
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
        /// If the current floor index is less than the target floor index, the passenger is moving upwards, and the direction is set to <see cref="Direction.Up"/>.
        /// Conversely, if the current floor index is greater than or equal to the target floor index, the direction is set to <see cref="Direction.Down"/>.
        /// This method does not return any value and directly modifies the <see cref="PassengerDirection"/> property.
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
        /// This method checks the direction of the specified elevator against the direction the passenger intends to go.
        /// If the elevator's direction matches the passenger's direction, it returns true, indicating that the elevator is suitable for the passenger's needs.
        /// Additionally, if the elevator's direction is 'None', it also returns true, as this indicates that the elevator is not currently assigned to any floors and can be considered available.
        /// If neither condition is met, the method returns false, indicating that the elevator is not appropriate for the passenger's intended direction.
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
        /// This method updates the passenger's position to the elevator's current Y position, flips the passenger's graphic horizontally, 
        /// and makes the passenger visible as they move towards the exit of the building. After reaching the exit, the passenger is made 
        /// invisible again, and it is noted that the object should ideally be disposed of instead of just being hidden. Additionally, 
        /// the passenger is removed from the list of people who require animation, indicating that no further animation is needed for this passenger.
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
        /// This method updates the passenger's position on the X-axis by moving it either left or right, depending on the current position relative to the destination.
        /// If the current X position is greater than the destination, the method moves the passenger left by decrementing the X coordinate in a loop until it reaches the destination.
        /// Conversely, if the current X position is less than the destination, it moves the passenger right by incrementing the X coordinate.
        /// During each step of the movement, there is a delay specified by <see cref="passengerAnimationDelay"/> to create an animation effect.
        /// The Y coordinate remains unchanged throughout this operation.
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

        

        #endregion


        #region EVENT HANDLERS

        /// <summary>
        /// Handles the event when a new passenger appears on the current floor.
        /// </summary>
        /// <param name="sender">The source of the event, typically the current floor.</param>
        /// <param name="e">An instance of <see cref="EventArgs"/> containing the event data.</param>
        /// <remarks>
        /// This method is triggered when a new passenger is detected on the current floor. 
        /// It first unsubscribes from the <c>NewPassengerAppeared</c> event to prevent further handling of this event, 
        /// as it is no longer needed after the initial detection. 
        /// Following this, it calls the <c>FindAnElevatorOrCallForANewOne</c> method to either locate an available elevator 
        /// or initiate a request for a new elevator if none are currently available. 
        /// This ensures that the passenger is attended to promptly.
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
        /// <param name="e">An instance of <see cref="ElevatorEventArgs"/> containing event data.</param>
        /// <remarks>
        /// This method is triggered when an elevator arrives at a designated floor or when it has space for additional passengers.
        /// It ensures thread safety by using a lock to prevent multiple elevators from processing the event simultaneously.
        /// 
        /// The method first checks the current status of the passenger. If the passenger is in the process of getting into the elevator, 
        /// the method returns immediately to avoid any conflicts. If the passenger is waiting for an elevator, it checks if the arriving 
        /// elevator can accommodate new passengers. If so, it updates the passenger's status to indicate they are getting in and 
        /// initiates the process of entering the elevator on a separate thread. If the elevator cannot take more passengers, it calls 
        /// for another elevator or finds an alternative solution.
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
